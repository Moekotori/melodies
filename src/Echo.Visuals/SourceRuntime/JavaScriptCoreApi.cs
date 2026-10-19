using System.Runtime.InteropServices;

namespace Echo.Visuals.SourceRuntime;

internal static unsafe partial class JavaScriptCoreApi
{
    private const string Library = "/System/Library/Frameworks/JavaScriptCore.framework/JavaScriptCore";
    [LibraryImport(Library)] internal static partial nint JSGlobalContextCreate(nint globalClass);
    [LibraryImport(Library)] internal static partial void JSGlobalContextRelease(nint context);
    [LibraryImport(Library)] internal static partial nint JSContextGetGlobalObject(nint context);
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)] internal static partial nint JSStringCreateWithUTF8CString(string value);
    [LibraryImport(Library)] internal static partial void JSStringRelease(nint value);
    [LibraryImport(Library)] internal static partial nuint JSStringGetMaximumUTF8CStringSize(nint value);
    [LibraryImport(Library)] internal static partial nuint JSStringGetUTF8CString(nint value, byte* buffer, nuint length);
    [LibraryImport(Library)] internal static partial nint JSObjectMakeFunctionWithCallback(nint context, nint name, delegate* unmanaged[Cdecl]<nint,nint,nint,nuint,nint*,nint*,nint> callback);
    [LibraryImport(Library)] internal static partial void JSObjectSetProperty(nint context, nint obj, nint name, nint value, uint attributes, nint* error);
    [LibraryImport(Library)] internal static partial nint JSObjectGetProperty(nint context, nint obj, nint name, nint* error);
    [LibraryImport(Library)] internal static partial nint JSEvaluateScript(nint context, nint source, nint obj, nint url, int line, nint* error);
    [LibraryImport(Library)] internal static partial nint JSObjectCallAsFunction(nint context, nint function, nint obj, nuint count, nint* args, nint* error);
    [LibraryImport(Library)] internal static partial int JSValueGetType(nint context, nint value);
    [LibraryImport(Library)] internal static partial nint JSValueMakeUndefined(nint context);
    [LibraryImport(Library)] internal static partial nint JSValueMakeNull(nint context);
    [LibraryImport(Library)] internal static partial nint JSValueMakeNumber(nint context, double value);
    [LibraryImport(Library)] internal static partial nint JSValueMakeBoolean(nint context, [MarshalAs(UnmanagedType.I1)] bool value);
    [LibraryImport(Library)] internal static partial nint JSValueMakeString(nint context, nint value);
    [LibraryImport(Library)] internal static partial nint JSValueToStringCopy(nint context, nint value, nint* error);
    [LibraryImport(Library)] internal static partial double JSValueToNumber(nint context, nint value, nint* error);
    [LibraryImport(Library)] [return: MarshalAs(UnmanagedType.I1)] internal static partial bool JSValueToBoolean(nint context, nint value);
    [LibraryImport(Library)] internal static partial void JSValueProtect(nint context, nint value);
    [LibraryImport(Library)] internal static partial void JSValueUnprotect(nint context, nint value);
}
