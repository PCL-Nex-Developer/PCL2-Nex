using System;

namespace PCL.Core.App.Plugins.JavaScript;

public sealed class JavaScriptVisualBasicFacade
{
    private const string ReplacementMessage = "pcl.vb.eval 已移除；请将 VB/C# 逻辑编译为 DLL，放入插件目录后通过 pcl.dotnet.loadAssembly('YourPlugin.dll') 引用，再用 pcl.dotnet.newObject/staticMember/type 调用。";

    public object? Eval(string code, object? globals = null) => throw new NotSupportedException(ReplacementMessage);

    public object? eval(string code, object? globals = null) => Eval(code, globals);

    public object? EvalExpression(string expression, object? globals = null) => throw new NotSupportedException(ReplacementMessage);

    public object? evalExpression(string expression, object? globals = null) => EvalExpression(expression, globals);
}