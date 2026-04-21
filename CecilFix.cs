using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;

public static class CecilFix
{
    public static void Run(string inputPath, string outputPath)
    {
        var bytes = File.ReadAllBytes(inputPath);
        var ms    = new MemoryStream(bytes);
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(inputPath));
        var asm = AssemblyDefinition.ReadAssembly(ms, new ReaderParameters { AssemblyResolver = resolver });

        int removed = RemoveFrom(asm.MainModule.Types, "top-level");
        foreach (TypeDefinition t in asm.MainModule.Types)
            removed += RemoveNested(t);

        Console.WriteLine($"Cecil: removed {removed} duplicate type definition(s)");
        asm.Write(outputPath);
        ms.Dispose();
    }

    static int RemoveFrom(Mono.Collections.Generic.Collection<TypeDefinition> col, string ctx)
    {
        var seen   = new HashSet<string>();
        var toKill = new List<TypeDefinition>();
        foreach (TypeDefinition t in col)
            if (!seen.Add(t.FullName)) toKill.Add(t);
        foreach (TypeDefinition t in toKill)
        {
            col.Remove(t);
            Console.WriteLine($"  [{ctx}] removed duplicate: {t.FullName}");
        }
        return toKill.Count;
    }

    static int RemoveNested(TypeDefinition parent)
    {
        if (!parent.HasNestedTypes) return 0;
        int n = RemoveFrom(parent.NestedTypes, parent.FullName);
        foreach (TypeDefinition c in parent.NestedTypes)
            n += RemoveNested(c);
        return n;
    }
}
