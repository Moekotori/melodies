#include <windows.h>
#include <shobjidl_core.h>
#include <SystemMediaTransportControlsInterop.h>

#include <winrt/Windows.Data.Json.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Media.h>
#include <winrt/Windows.Storage.h>
#include <winrt/Windows.Storage.Streams.h>

#include <algorithm>
#include <chrono>
#include <condition_variable>
#include <iostream>
#include <cmath>
#include <mutex>
#include <queue>
#include <string>
#include <thread>

using namespace winrt;
using namespace Windows::Data::Json;
using namespace Windows::Foundation;
using namespace Windows::Media;
using namespace Windows::Storage;
using namespace Windows::Storage::Streams;

namespace
{
constexpr wchar_t windowClassName[] = L"EchoSmtcHostWindow";
constexpr wchar_t defaultAppUserModelId[] = L"app.echo.steam";
constexpr UINT processInputMessage = WM_APP + 1;

TimeSpan secondsToTimeSpan(double seconds)
{
    const auto safeSeconds = std::max(0.0, seconds);
    return std::chrono::duration_cast<TimeSpan>(std::chrono::duration<double>(safeSeconds));
}

double timeSpanToSeconds(TimeSpan value)
{
    return std::max(0.0, std::chrono::duration<double>(value).count());
}

std::wstring widen(const std::string& value)
{
    if (value.empty())
    {
        return {};
    }

    const auto count = MultiByteToWideChar(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), nullptr, 0);
    if (count <= 0)
    {
        return {};
    }

    std::wstring result(static_cast<size_t>(count), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), result.data(), count);
    return result;
}

std::string escapeJson(const std::string& value)
{
    std::string escaped;
    escaped.reserve(value.size() + 8);

    for (const auto ch : value)
    {
        switch (ch)
        {
        case '\\':
            escaped += "\\\\";
            break;
        case '"':
            escaped += "\\\"";
            break;
        case '\b':
            escaped += "\\b";
            break;
        case '\f':
            escaped += "\\f";
            break;
        case '\n':
            escaped += "\\n";
            break;
        case '\r':
            escaped += "\\r";
            break;
        case '\t':
            escaped += "\\t";
            break;
        default:
            if (static_cast<unsigned char>(ch) < 0x20)
            {
                escaped += "\\u00";
                const char* hex = "0123456789abcdef";
                escaped += hex[(ch >> 4) & 0x0f];
                escaped += hex[ch & 0x0f];
            }
            else
            {
                escaped += ch;
            }
        }
    }

    return escaped;
}

std::string commandName(SystemMediaTransportControlsButton button)
{
    switch (button)
    {
    case SystemMediaTransportControlsButton::Play:
        return "play";
    case SystemMediaTransportControlsButton::Pause:
        return "pause";
    case SystemMediaTransportControlsButton::Previous:
        return "previous";
    case SystemMediaTransportControlsButton::Next:
        return "next";
    case SystemMediaTransportControlsButton::Stop:
        return "stop";
    default:
        return {};
    }
}

MediaPlaybackStatus playbackStatusFromString(const hstring& state)
{
    if (state == L"playing")
    {
        return MediaPlaybackStatus::Playing;
    }

    if (state == L"loading")
    {
        return MediaPlaybackStatus::Changing;
    }

    if (state == L"paused")
    {
        return MediaPlaybackStatus::Paused;
    }

    if (state == L"idle" || state == L"stopped" || state == L"ended" || state == L"error")
    {
        return MediaPlaybackStatus::Stopped;
    }

    return MediaPlaybackStatus::Closed;
}

MediaPlaybackAutoRepeatMode repeatModeFromString(const hstring& mode)
{
    if (mode == L"track")
    {
        return MediaPlaybackAutoRepeatMode::Track;
    }

    if (mode == L"list")
    {
        return MediaPlaybackAutoRepeatMode::List;
    }

    return MediaPlaybackAutoRepeatMode::None;
}

std::string repeatModeName(MediaPlaybackAutoRepeatMode mode)
{
    switch (mode)
    {
    case MediaPlaybackAutoRepeatMode::Track:
        return "track";
    case MediaPlaybackAutoRepeatMode::List:
        return "list";
    default:
        return "none";
    }
}

std::wstring resolveAppUserModelId(int argc, char** argv)
{
    constexpr char prefix[] = "--aumid=";
    constexpr size_t prefixLength = sizeof(prefix) - 1;

    for (int index = 1; index < argc; ++index)
    {
        const std::string argument = argv[index] ? argv[index] : "";
        if (argument.compare(0, prefixLength, prefix) == 0 && argument.size() > prefixLength)
        {
            return widen(argument.substr(prefixLength));
        }
    }

    return defaultAppUserModelId;
}

class SmtcHost
{
public:
    HWND window = nullptr;

    void initialize(HWND hwnd)
    {
        window = hwnd;

        auto interop = get_activation_factory<SystemMediaTransportControls, ISystemMediaTransportControlsInterop>();
        SystemMediaTransportControls nextControls{ nullptr };
        check_hresult(interop->GetForWindow(
            hwnd,
            guid_of<SystemMediaTransportControls>(),
            put_abi(nextControls)));

        controls = nextControls;
        buttonToken = controls.ButtonPressed({ this, &SmtcHost::onButtonPressed });
        playbackPositionToken = controls.PlaybackPositionChangeRequested({ this, &SmtcHost::onPlaybackPositionChangeRequested });
        shuffleToken = controls.ShuffleEnabledChangeRequested({ this, &SmtcHost::onShuffleEnabledChangeRequested });
        repeatToken = controls.AutoRepeatModeChangeRequested({ this, &SmtcHost::onAutoRepeatModeChangeRequested });
        // Stay unpublished until the first setMetadata. An enabled session with an
        // empty DisplayUpdater still becomes the shell's current session, and the
        // media overlay then falls back to rendering our raw process name.
        controls.IsEnabled(false);
        controls.IsPlayEnabled(true);
        controls.IsPauseEnabled(true);
        controls.IsPreviousEnabled(true);
        controls.IsNextEnabled(true);
        controls.IsStopEnabled(true);
        controls.IsChannelUpEnabled(false);
        controls.IsChannelDownEnabled(false);
        controls.IsFastForwardEnabled(false);
        controls.IsRewindEnabled(false);
        controls.PlaybackStatus(MediaPlaybackStatus::Closed);
        controls.ShuffleEnabled(false);
        controls.AutoRepeatMode(MediaPlaybackAutoRepeatMode::None);
        controls.PlaybackRate(1.0);
    }

    void emitReady()
    {
        std::lock_guard lock(outputMutex);
        std::cout
            << "{\"type\":\"ready\",\"protocolVersion\":1,\"capabilities\":{"
            << "\"metadata\":true,\"timeline\":true,\"enabledActions\":true,"
            << "\"seekCommands\":true,\"localArtwork\":true,"
            << "\"playbackRate\":true,\"playbackModes\":true}}"
            << std::endl;
    }

    void enqueue(std::string line)
    {
        {
            std::lock_guard lock(inputMutex);
            inputQueue.push(std::move(line));
        }

        PostMessageW(window, processInputMessage, 0, 0);
    }

    void drainInput()
    {
        for (;;)
        {
            std::string line;
            {
                std::lock_guard lock(inputMutex);
                if (inputQueue.empty())
                {
                    return;
                }
                line = std::move(inputQueue.front());
                inputQueue.pop();
            }

            handleLine(line);
        }
    }

    void requestQuit()
    {
        if (window)
        {
            PostMessageW(window, WM_CLOSE, 0, 0);
        }
    }

    void dispose()
    {
        if (controls)
        {
            try
            {
                controls.PlaybackStatus(MediaPlaybackStatus::Closed);
                controls.IsEnabled(false);
                controls.ButtonPressed(buttonToken);
                controls.PlaybackPositionChangeRequested(playbackPositionToken);
                controls.ShuffleEnabledChangeRequested(shuffleToken);
                controls.AutoRepeatModeChangeRequested(repeatToken);
            }
            catch (...)
            {
            }
        }

        thumbnail = nullptr;
        thumbnailPath.clear();
        controls = nullptr;
    }

private:
    SystemMediaTransportControls controls{ nullptr };
    event_token buttonToken{};
    event_token playbackPositionToken{};
    event_token shuffleToken{};
    event_token repeatToken{};
    bool seekEnabled = true;
    bool sessionPublished = false;
    // The shell re-reads the thumbnail on every DisplayUpdater.Update(); keeping
    // the stream reference for the current cover avoids reopening the file when
    // only text fields (title, lyric line, ...) change.
    RandomAccessStreamReference thumbnail{ nullptr };
    std::wstring thumbnailPath;
    std::mutex inputMutex;
    std::queue<std::string> inputQueue;
    std::mutex outputMutex;

    void emitCommand(const std::string& command)
    {
        if (command.empty())
        {
            return;
        }

        std::lock_guard lock(outputMutex);
        std::cout << "{\"type\":\"command\",\"command\":\"" << escapeJson(command) << "\"}" << std::endl;
    }

    void emitShuffleCommand(bool enabled)
    {
        std::lock_guard lock(outputMutex);
        std::cout << "{\"type\":\"command\",\"command\":\"setShuffle\",\"enabled\":" << (enabled ? "true" : "false") << "}" << std::endl;
    }

    void emitRepeatCommand(MediaPlaybackAutoRepeatMode mode)
    {
        std::lock_guard lock(outputMutex);
        std::cout << "{\"type\":\"command\",\"command\":\"setRepeat\",\"mode\":\"" << repeatModeName(mode) << "\"}" << std::endl;
    }

    void emitSeekCommand(double positionSeconds)
    {
        const auto safePositionSeconds = std::isfinite(positionSeconds) ? std::max(0.0, positionSeconds) : 0.0;
        std::lock_guard lock(outputMutex);
        std::cout << "{\"type\":\"command\",\"command\":\"seek\",\"positionSeconds\":" << safePositionSeconds << "}" << std::endl;
    }

    void emitError(const std::string& message)
    {
        std::lock_guard lock(outputMutex);
        std::cout << "{\"type\":\"error\",\"message\":\"" << escapeJson(message) << "\"}" << std::endl;
    }

    void onButtonPressed(
        SystemMediaTransportControls const&,
        SystemMediaTransportControlsButtonPressedEventArgs const& args)
    {
        emitCommand(commandName(args.Button()));
    }

    void onPlaybackPositionChangeRequested(
        SystemMediaTransportControls const&,
        PlaybackPositionChangeRequestedEventArgs const& args)
    {
        emitSeekCommand(timeSpanToSeconds(args.RequestedPlaybackPosition()));
    }

    void onShuffleEnabledChangeRequested(
        SystemMediaTransportControls const&,
        ShuffleEnabledChangeRequestedEventArgs const& args)
    {
        emitShuffleCommand(args.RequestedShuffleEnabled());
    }

    void onAutoRepeatModeChangeRequested(
        SystemMediaTransportControls const&,
        AutoRepeatModeChangeRequestedEventArgs const& args)
    {
        emitRepeatCommand(args.RequestedAutoRepeatMode());
    }

    hstring getString(JsonObject const& object, const wchar_t* name, const wchar_t* fallback = L"")
    {
        auto value = object.TryLookup(name);
        if (!value || value.ValueType() != JsonValueType::String)
        {
            return fallback;
        }

        return value.GetString();
    }

    bool getBool(JsonObject const& object, const wchar_t* name, bool fallback = false)
    {
        auto value = object.TryLookup(name);
        if (!value || value.ValueType() != JsonValueType::Boolean)
        {
            return fallback;
        }

        return value.GetBoolean();
    }

    double getNumber(JsonObject const& object, const wchar_t* name, double fallback = 0)
    {
        auto value = object.TryLookup(name);
        if (!value || value.ValueType() != JsonValueType::Number)
        {
            return fallback;
        }

        return value.GetNumber();
    }

    void handleLine(const std::string& line)
    {
        if (line.empty())
        {
            return;
        }

        try
        {
            auto object = JsonObject::Parse(hstring(widen(line)));
            const auto type = getString(object, L"type");

            if (type == L"setMetadata")
            {
                setMetadata(object);
            }
            else if (type == L"setPlaybackState")
            {
                controls.PlaybackStatus(playbackStatusFromString(getString(object, L"state")));
            }
            else if (type == L"setTimeline")
            {
                setTimeline(object);
            }
            else if (type == L"setEnabledActions")
            {
                setEnabledActions(object);
            }
            else if (type == L"setPlaybackRate")
            {
                setPlaybackRate(getNumber(object, L"rate", 1.0));
            }
            else if (type == L"setPlaybackModes")
            {
                setPlaybackModes(object);
            }
            else if (type == L"clear")
            {
                clearDisplay();
            }
            else if (type == L"dispose")
            {
                dispose();
                requestQuit();
            }
            else if (type == L"shutdown" || getString(object, L"command") == L"shutdown")
            {
                dispose();
                requestQuit();
            }
        }
        catch (hresult_error const& error)
        {
            emitError(to_string(error.message()));
        }
        catch (std::exception const& error)
        {
            emitError(error.what());
        }
        catch (...)
        {
            emitError("Unknown SMTC host error");
        }
    }

    void setMetadata(JsonObject const& object)
    {
        auto updater = controls.DisplayUpdater();
        updater.Type(MediaPlaybackType::Music);
        updater.AppMediaId(L"ECHO");

        auto music = updater.MusicProperties();
        music.Title(getString(object, L"title", L"ECHO"));
        music.Artist(getString(object, L"artist"));
        music.AlbumTitle(getString(object, L"album"));
        music.AlbumArtist(getString(object, L"albumArtist"));

        updater.Thumbnail(resolveThumbnail(getString(object, L"coverPath")));
        updater.Update();
        setSessionPublished(true);

        // Follow-up metadata (artwork resolved later, lyric line changed) omits the
        // timeline so a stale position never overrides the live setTimeline stream.
        if (object.TryLookup(L"positionSeconds") || object.TryLookup(L"durationSeconds"))
        {
            updateTimeline(getNumber(object, L"positionSeconds"), getNumber(object, L"durationSeconds"));
        }
    }

    RandomAccessStreamReference resolveThumbnail(const hstring& coverPath)
    {
        if (coverPath.empty())
        {
            thumbnail = nullptr;
            thumbnailPath.clear();
            return nullptr;
        }

        const std::wstring nextPath{ coverPath };
        if (thumbnail && nextPath == thumbnailPath)
        {
            return thumbnail;
        }

        try
        {
            auto file = StorageFile::GetFileFromPathAsync(coverPath).get();
            thumbnail = RandomAccessStreamReference::CreateFromFile(file);
            thumbnailPath = nextPath;
        }
        catch (...)
        {
            thumbnail = nullptr;
            thumbnailPath.clear();
        }

        return thumbnail;
    }

    void setPlaybackRate(double rate)
    {
        const auto safeRate = std::isfinite(rate) && rate > 0 ? rate : 1.0;
        controls.PlaybackRate(safeRate);
    }

    void setPlaybackModes(JsonObject const& object)
    {
        controls.ShuffleEnabled(getBool(object, L"shuffle", false));
        controls.AutoRepeatMode(repeatModeFromString(getString(object, L"repeat", L"none")));
    }

    void setSessionPublished(bool published)
    {
        if (sessionPublished == published)
        {
            return;
        }

        sessionPublished = published;
        controls.IsEnabled(published);
    }

    void setTimeline(JsonObject const& object)
    {
        updateTimeline(getNumber(object, L"positionSeconds"), getNumber(object, L"durationSeconds"));
    }

    void updateTimeline(double positionSeconds, double durationSeconds)
    {
        const auto safeDuration = std::max(0.0, durationSeconds);
        const auto safePosition = std::max(0.0, std::min(positionSeconds, safeDuration > 0 ? safeDuration : positionSeconds));
        const auto canSeek = seekEnabled && safeDuration > 0;

        SystemMediaTransportControlsTimelineProperties timeline;
        timeline.StartTime(secondsToTimeSpan(0));
        timeline.MinSeekTime(secondsToTimeSpan(canSeek ? 0 : safePosition));
        timeline.Position(secondsToTimeSpan(safePosition));
        timeline.EndTime(secondsToTimeSpan(safeDuration));
        timeline.MaxSeekTime(secondsToTimeSpan(canSeek ? safeDuration : safePosition));
        controls.UpdateTimelineProperties(timeline);
    }

    void setEnabledActions(JsonObject const& object)
    {
        controls.IsPlayEnabled(getBool(object, L"play", true));
        controls.IsPauseEnabled(getBool(object, L"pause", true));
        controls.IsPreviousEnabled(getBool(object, L"previous", true));
        controls.IsNextEnabled(getBool(object, L"next", true));
        controls.IsStopEnabled(true);
        controls.IsFastForwardEnabled(false);
        controls.IsRewindEnabled(false);
        seekEnabled = getBool(object, L"seek", true);
    }

    void clearDisplay()
    {
        auto updater = controls.DisplayUpdater();
        updater.ClearAll();
        updater.Update();
        thumbnail = nullptr;
        thumbnailPath.clear();
        controls.PlaybackStatus(MediaPlaybackStatus::Closed);
        updateTimeline(0, 0);
        setSessionPublished(false);
    }
};

LRESULT CALLBACK windowProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
{
    auto host = reinterpret_cast<SmtcHost*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));

    if (message == WM_NCCREATE)
    {
        auto createStruct = reinterpret_cast<CREATESTRUCTW*>(lParam);
        host = static_cast<SmtcHost*>(createStruct->lpCreateParams);
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(host));
        return TRUE;
    }

    if (!host)
    {
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    switch (message)
    {
    case processInputMessage:
        host->drainInput();
        return 0;
    case WM_CLOSE:
        host->dispose();
        DestroyWindow(hwnd);
        return 0;
    case WM_DESTROY:
        PostQuitMessage(0);
        return 0;
    default:
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }
}
}

int main(int argc, char** argv)
{
    init_apartment(apartment_type::single_threaded);
    // Electron passes its own AUMID so the shell attributes this session to the
    // ECHO app entry (and dev builds stay distinct from the installed app).
    SetCurrentProcessExplicitAppUserModelID(resolveAppUserModelId(argc, argv).c_str());

    SmtcHost host;
    HINSTANCE instance = GetModuleHandleW(nullptr);
    WNDCLASSW windowClass{};
    windowClass.lpfnWndProc = windowProc;
    windowClass.hInstance = instance;
    windowClass.lpszClassName = windowClassName;

    RegisterClassW(&windowClass);

    HWND hwnd = CreateWindowExW(
        WS_EX_TOOLWINDOW,
        windowClassName,
        L"ECHO SMTC Host",
        WS_OVERLAPPED,
        0,
        0,
        0,
        0,
        nullptr,
        nullptr,
        instance,
        &host);

    if (!hwnd)
    {
        std::cerr << "[echo-smtc-host] failed to create hidden window" << std::endl;
        return 1;
    }

    try
    {
        host.initialize(hwnd);
        host.emitReady();
    }
    catch (hresult_error const& error)
    {
        std::cerr << "[echo-smtc-host] " << to_string(error.message()) << std::endl;
        DestroyWindow(hwnd);
        return 1;
    }

    std::thread inputThread([&host]() {
        std::string line;
        while (std::getline(std::cin, line))
        {
            host.enqueue(std::move(line));
        }

        host.requestQuit();
    });

    MSG message{};
    while (GetMessageW(&message, nullptr, 0, 0) > 0)
    {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }

    if (inputThread.joinable())
    {
        inputThread.join();
    }

    return 0;
}
