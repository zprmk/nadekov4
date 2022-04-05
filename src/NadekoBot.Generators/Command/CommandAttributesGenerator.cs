﻿#nullable enable
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NadekoBot.Generators.Command;

[Generator]
public class CommandAttributesGenerator : IIncrementalGenerator
{
    public const string ATTRIBUTE = @"// <AutoGenerated />

namespace NadekoBot.Common;

[System.AttributeUsage(System.AttributeTargets.Method)]
public class CmdAttribute : System.Attribute
{
    
}";

    public class MethodModel
    {
        public string? Namespace { get; }
        public IReadOnlyCollection<string> Classes { get; }
        public string ReturnType { get; }
        public string MethodName { get; }
        public IEnumerable<string> Params { get; }

        public MethodModel(string? ns, IReadOnlyCollection<string> classes, string returnType, string methodName, IEnumerable<string> @params)
        {
            Namespace = ns;
            Classes = classes;
            ReturnType = returnType;
            MethodName = methodName;
            Params = @params;
        }
    }

    public class FileModel
    {
        public string? Namespace { get; }
        public IReadOnlyCollection<string> ClassHierarchy { get; }
        public IReadOnlyCollection<MethodModel> Methods { get; }

        public FileModel(string? ns, IReadOnlyCollection<string> classHierarchy, IReadOnlyCollection<MethodModel> methods)
        {
            Namespace = ns;
            ClassHierarchy = classHierarchy;
            Methods = methods;
        }
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
// #if DEBUG
//         SpinWait.SpinUntil(() => Debugger.IsAttached);
// #endif
        context.RegisterPostInitializationOutput(static ctx => ctx.AddSource(
            "CmdAttribute.g.cs",
            SourceText.From(ATTRIBUTE, Encoding.UTF8)));

        var methods = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
                static (ctx, cancel) => Transform(ctx, cancel))
            .Where(static m => m is not null)
            .Where(static m => m?.ChildTokens().Any(static x => x.IsKind(SyntaxKind.PublicKeyword)) ?? false);

        var compilationMethods = context.CompilationProvider.Combine(methods.Collect());

        context.RegisterSourceOutput(compilationMethods,
            static (ctx, tuple) => RegisterAction(in ctx, tuple.Left, in tuple.Right));
    }

    private static void RegisterAction(in SourceProductionContext ctx,
        Compilation comp,
        in ImmutableArray<MethodDeclarationSyntax?> methods)
    {
        if (methods is { IsDefaultOrEmpty: true })
            return;

        var models = GetModels(comp, methods, ctx.CancellationToken);

        foreach (var model in models)
        {
            var name = $"{model.Namespace}.{string.Join(".", model.ClassHierarchy)}.g.cs";
            try
            {
                Debug.WriteLine($"Writing {name}");
                var source = GetSourceText(model);
                ctx.AddSource(name, SourceText.From(source, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error writing source file {name}\n" + ex);
            }
        }
    }

    private static string GetSourceText(FileModel model)
    {
        using var sw = new StringWriter();
        using var tw = new IndentedTextWriter(sw);
        
        tw.WriteLine("// <AutoGenerated />");
        tw.WriteLine("#pragma warning disable CS1066");

        if (model.Namespace is not null)
        {
            tw.WriteLine($"namespace {model.Namespace};");
            tw.WriteLine();
        }

        foreach (var className in model.ClassHierarchy)
        {
            tw.WriteLine($"public partial class {className}");
            tw.WriteLine("{");
            tw.Indent ++;
        }
        
        foreach (var method in model.Methods)
        {
            tw.WriteLine("[NadekoCommand]");
            tw.WriteLine("[NadekoDescription]");
            tw.WriteLine("[Aliases]");
            tw.WriteLine($"public partial {method.ReturnType} {method.MethodName}({string.Join(", ", method.Params)});");
        }
        
        foreach (var _ in model.ClassHierarchy)
        {
            tw.Indent --;
            tw.WriteLine("}");
        }
        
        tw.Flush();
        return sw.ToString();
    }

    private static IReadOnlyCollection<FileModel> GetModels(Compilation compilation,
        in ImmutableArray<MethodDeclarationSyntax?> inputMethods,
        CancellationToken cancel)
    {
        var models = new List<FileModel>();

        var methods = inputMethods
            .Where(static x => x is not null)
            .Distinct();
        
        var methodModels = methods
            .Select(x => MethodDeclarationToMethodModel(compilation, x!));

        var groups = methodModels
            .GroupBy(static x => $"{x.Namespace}.{string.Join(".", x.Classes)}");
        
        foreach (var group in groups)
        {
            if (cancel.IsCancellationRequested)
                return new Collection<FileModel>();

            if (group is null)
                continue;
            
            var elems = group.ToList();
            if (elems.Count is 0)
                continue;

            var model = new FileModel(
                methods: elems,
                ns: elems[0].Namespace,
                classHierarchy: elems[0].Classes
            );
            
            models.Add(model);
        }


        return models;
    }

    private static MethodModel MethodDeclarationToMethodModel(Compilation comp, MethodDeclarationSyntax decl)
    {
        // SpinWait.SpinUntil(static () => Debugger.IsAttached);

        var semanticModel = comp.GetSemanticModel(decl.SyntaxTree);
        var methodModel = new MethodModel(
            @params: decl.ParameterList.Parameters
                .Where(p => p.Type is not null)
                .Select(p =>
                {
                    var prefix = p.Modifiers.Any(static x => x.IsKind(SyntaxKind.ParamsKeyword))
                        ? "params "
                        : string.Empty;

                    var type = semanticModel
                        .GetTypeInfo(p.Type!)
                        .Type
                        ?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);


                    var name = p.Identifier.Text;

                    var suffix = string.Empty;
                    if (p.Default is not null)
                    {
                        if (p.Default.Value is LiteralExpressionSyntax)
                        {
                            suffix = " = " + p.Default.Value;
                        }
                        else if (p.Default.Value is MemberAccessExpressionSyntax maes)
                        {
                            var maesSemModel = comp.GetSemanticModel(maes.SyntaxTree);
                            var sym = maesSemModel.GetSymbolInfo(maes.Name);
                            if (sym.Symbol is null)
                            {
                                suffix = " = " + p.Default.Value;
                            }
                            else
                            {
                                suffix = " = " + sym.Symbol.ToDisplayString();
                            }
                        }
                    }

                    return $"{prefix}{type} {name}{suffix}";
                })
                .ToList(),
            methodName: decl.Identifier.Text,
            returnType: decl.ReturnType.ToString(),
            ns: GetNamespace(decl),
            classes: GetClasses(decl)
        );

        return methodModel;
    }

    //https://github.com/andrewlock/NetEscapades.EnumGenerators/blob/main/src/NetEscapades.EnumGenerators/EnumGenerator.cs
    static string? GetNamespace(MethodDeclarationSyntax declarationSyntax)
    {
        // determine the namespace the class is declared in, if any
        string? nameSpace = null;
        var parentOfInterest = declarationSyntax.Parent;
        while (parentOfInterest is not null)
        {
            parentOfInterest = parentOfInterest.Parent;
            
            if (parentOfInterest is BaseNamespaceDeclarationSyntax ns)
            {
                nameSpace = ns.Name.ToString();
                while (true)
                {
                    if (ns.Parent is not NamespaceDeclarationSyntax parent)
                    {
                        break;
                    }

                    ns = parent;
                    nameSpace = $"{ns.Name}.{nameSpace}";
                }

                return nameSpace;
            }
            
        }
        
        return nameSpace;
    }
    
    static IReadOnlyCollection<string> GetClasses(MethodDeclarationSyntax declarationSyntax)
    {
        // determine the namespace the class is declared in, if any
        var classes = new LinkedList<string>();
        var parentOfInterest = declarationSyntax.Parent;
        while (parentOfInterest is not null)
        {
            if (parentOfInterest is ClassDeclarationSyntax cds)
            {
                classes.AddFirst(cds.Identifier.ToString());
            }

            parentOfInterest = parentOfInterest.Parent;
        }

        Debug.WriteLine($"Method {declarationSyntax.Identifier.Text} has {classes.Count} classes");
        
        return classes;
    }

    private static MethodDeclarationSyntax? Transform(GeneratorSyntaxContext ctx, CancellationToken cancel)
    {
        var methodDecl = ctx.Node as MethodDeclarationSyntax;
        if (methodDecl is null)
            return default;

        foreach (var attListSyntax in methodDecl.AttributeLists)
        {
            foreach (var attSyntax in attListSyntax.Attributes)
            {
                if (cancel.IsCancellationRequested)
                    return default;

                var symbol = ctx.SemanticModel.GetSymbolInfo(attSyntax).Symbol;
                if (symbol is not IMethodSymbol attSymbol)
                    continue;

                if (attSymbol.ContainingType.ToDisplayString() == "NadekoBot.Common.CmdAttribute")
                    return methodDecl;
            }
        }

        return default;
    }
}