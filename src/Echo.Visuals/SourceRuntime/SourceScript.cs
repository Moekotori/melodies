using Jint;
using Jint.Native;
using Jint.Runtime.Interop;

namespace Echo.Visuals.SourceRuntime;

internal interface ISourceScript : IDisposable
{
    void Set(string name, string value);
    void Function(string name, Func<JsValue[], JsValue> callback);
    void Execute(string source);
    JsValue Invoke(string name, params JsValue[] arguments);
}

internal sealed class ManagedSourceScript : ISourceScript
{
    private readonly Engine engine;
    public ManagedSourceScript(CancellationToken cancellation) => engine = new Engine(o => o.LimitMemory(128 * 1024 * 1024)
        .TimeoutInterval(TimeSpan.FromSeconds(4)).MaxStatements(8_000_000).CancellationToken(cancellation));
    public void Set(string name, string value) => engine.SetValue(name, value);
    public void Function(string name, Func<JsValue[], JsValue> callback)
        => engine.SetValue(name, new ClrFunction(engine, name, (_, args) => callback(args)));
    public void Execute(string source) => engine.Execute(source);
    public JsValue Invoke(string name, params JsValue[] arguments) => engine.Invoke(name, arguments);
    public void Dispose() => engine.Dispose();
}
