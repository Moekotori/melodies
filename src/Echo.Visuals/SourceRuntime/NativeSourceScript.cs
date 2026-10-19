using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Jint.Native;
using static Echo.Visuals.SourceRuntime.JavaScriptCoreApi;

namespace Echo.Visuals.SourceRuntime;

// Public JavaScriptCore C API. One context stays on its owning scene worker; only primitive callbacks cross the boundary.
internal sealed unsafe class NativeSourceScript : ISourceScript
{
    private static readonly ConcurrentDictionary<nint, NativeSourceScript> contexts = [];
    private readonly Dictionary<nint, Func<JsValue[], JsValue>> callbacks = [];
    private readonly Dictionary<string, nint> functions = [];
    private readonly CancellationToken cancellation;
    private nint context;
    private long entered;
    public NativeSourceScript(CancellationToken cancellation)
    {
        this.cancellation = cancellation;
        context = JSGlobalContextCreate(0);
        if (context == 0) throw new InvalidOperationException("Unable to create source-scene JavaScript context.");
        contexts[context] = this;
    }
    public void Set(string name, string value) => Property(name, Value(value));
    public void Function(string name, Func<JsValue[], JsValue> callback)
    {
        var key = JSStringCreateWithUTF8CString(name);
        try
        {
            var function = JSObjectMakeFunctionWithCallback(context, key, &Callback);
            callbacks.Add(function, callback); Property(name, function);
        }
        finally { JSStringRelease(key); }
    }
    private void Property(string name, nint value)
    {
        var key = JSStringCreateWithUTF8CString(name); nint error = 0;
        try { JSObjectSetProperty(context, JSContextGetGlobalObject(context), key, value, 0, &error); Check(error); }
        finally { JSStringRelease(key); }
    }
    public void Execute(string source)
    {
        entered = Stopwatch.GetTimestamp();
        var text = JSStringCreateWithUTF8CString(source); nint error = 0;
        try { JSEvaluateScript(context, text, 0, 0, 1, &error); Check(error); }
        finally { JSStringRelease(text); }
    }
    public JsValue Invoke(string name, params JsValue[] arguments)
    {
        cancellation.ThrowIfCancellationRequested(); entered = Stopwatch.GetTimestamp();
        nint error = 0;
        if (!functions.TryGetValue(name, out var function))
        {
            var key = JSStringCreateWithUTF8CString(name);
            try { function = JSObjectGetProperty(context, JSContextGetGlobalObject(context), key, &error); Check(error); }
            finally { JSStringRelease(key); }
            JSValueProtect(context, function); functions[name] = function;
        }
        var values = stackalloc nint[arguments.Length];
        for (var i = 0; i < arguments.Length; i++) { values[i] = Value(arguments[i]); JSValueProtect(context, values[i]); }
        try
        {
            var result = JSObjectCallAsFunction(context, function, 0, (nuint)arguments.Length, values, &error);
            Check(error); return Read(result);
        }
        finally { for (var i = 0; i < arguments.Length; i++) JSValueUnprotect(context, values[i]); }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint Callback(nint context, nint function, nint self, nuint count, nint* arguments, nint* exception)
    {
        var owner = contexts[context];
        try
        {
            owner.cancellation.ThrowIfCancellationRequested();
            if (count > 32 || Stopwatch.GetElapsedTime(owner.entered).TotalSeconds > 4)
                throw new InvalidOperationException("Source lyric scene operation budget exceeded.");
            var values = new JsValue[(int)count];
            for (var i = 0; i < values.Length; i++) values[i] = owner.Read(arguments[i]);
            return owner.Value(owner.callbacks[function](values));
        }
        catch (Exception error)
        {
            *exception = owner.Value(error.Message);
            return JSValueMakeUndefined(context);
        }
    }
    private JsValue Read(nint value) => JSValueGetType(context, value) switch
    {
        0 => JsValue.Undefined, 1 => JsValue.Null, 2 => JSValueToBoolean(context, value),
        3 => JSValueToNumber(context, value, null), 4 => String(value),
        _ => throw new InvalidDataException("Only primitive source-scene values may cross the native boundary.")
    };
    private nint Value(JsValue value)
    {
        if (value.IsUndefined()) return JSValueMakeUndefined(context);
        if (value.IsNull()) return JSValueMakeNull(context);
        if (value.IsNumber()) return JSValueMakeNumber(context, value.AsNumber());
        if (value.IsBoolean()) return JSValueMakeBoolean(context, value.AsBoolean());
        if (value.IsString()) return Value(value.AsString());
        throw new InvalidDataException("Only primitive source-scene values may cross the native boundary.");
    }
    private nint Value(string value)
    {
        var text = JSStringCreateWithUTF8CString(value);
        try { return JSValueMakeString(context, text); }
        finally { JSStringRelease(text); }
    }
    private string String(nint value)
    {
        var text = JSValueToStringCopy(context, value, null);
        try
        {
            var length = JSStringGetMaximumUTF8CStringSize(text);
            if (length > 4 * 1024 * 1024) throw new InvalidDataException("Source scene string budget exceeded.");
            var bytes = new byte[(int)length];
            fixed (byte* buffer = bytes)
            {
                var used = JSStringGetUTF8CString(text, buffer, length);
                return Encoding.UTF8.GetString(bytes.AsSpan(0, (int)used - 1));
            }
        }
        finally { JSStringRelease(text); }
    }
    private void Check(nint error) { if (error != 0) throw new InvalidOperationException(String(error)); }
    public void Dispose()
    {
        if (context == 0) return;
        foreach (var function in functions.Values) JSValueUnprotect(context, function);
        functions.Clear(); callbacks.Clear(); contexts.TryRemove(context, out _);
        JSGlobalContextRelease(context); context = 0;
    }
}
