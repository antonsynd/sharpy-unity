// Lists the public types of Sharpy.Stdlib.dll that Sharpy.Core.dll does not
// define, each with the Sharpy module that owns it. Run by update_toolchain.py:
//
//     dotnet run stdlib_types.cs -- <Sharpy.Stdlib.dll> <Sharpy.Stdlib.pdb> <Sharpy.Core.dll>
//
// Output: one "<CLR full name>\t<module>" line per type, sorted (ordinal).
//
// The owning module comes from the type's own [SharpyModule("m")] or
// [SharpyModuleType("m", ...)] attribute. A type with neither (helpers such as
// Sharpy.LruCache) takes the module of the stdlib folder its source file is in:
// each folder's __Init__.cs declares that folder's module, and the folder is
// read from the portable PDB. Exits 1 if any type cannot be mapped.

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

if (args.Length != 3)
{
    Console.Error.WriteLine("usage: stdlib_types.cs <Sharpy.Stdlib.dll> <Sharpy.Stdlib.pdb> <Sharpy.Core.dll>");
    return 2;
}

// Roslyn's custom debug info kind for types whose source has no method bodies.
var typeDefinitionDocuments = new Guid("932E74BC-DBA9-4478-8D46-0F32A7BAB3D3");

using var stdlibPe = new PEReader(File.OpenRead(args[0]));
using var corePe = new PEReader(File.OpenRead(args[2]));
using var pdbProvider = MetadataReaderProvider.FromPortablePdbStream(File.OpenRead(args[1]));
var stdlib = stdlibPe.GetMetadataReader();
var core = corePe.GetMetadataReader();
var pdb = pdbProvider.GetMetadataReader();

var coreTypes = new HashSet<string>(StringComparer.Ordinal);
foreach (var handle in core.TypeDefinitions)
{
    coreTypes.Add(FullName(core, core.GetTypeDefinition(handle)));
}

var owners = new SortedDictionary<string, string>(StringComparer.Ordinal);
var unattributed = new List<(string Name, TypeDefinitionHandle Handle)>();
var folderModules = new Dictionary<string, string>(StringComparer.Ordinal);

foreach (var handle in stdlib.TypeDefinitions)
{
    var type = stdlib.GetTypeDefinition(handle);
    if (!type.GetDeclaringType().IsNil
        || (type.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public)
    {
        continue;
    }

    var name = FullName(stdlib, type);
    if (coreTypes.Contains(name))
    {
        continue;
    }

    var (module, declaresModule) = OwningModule(type);
    if (module == null)
    {
        unattributed.Add((name, handle));
        continue;
    }

    owners[name] = module;

    if (declaresModule)
    {
        foreach (var document in Documents(handle))
        {
            if (Path.GetFileName(document) == "__Init__.cs")
            {
                folderModules[Folder(document)] = module;
            }
        }
    }
}

var failed = false;
foreach (var (name, handle) in unattributed)
{
    var modules = Documents(handle)
        .Select(d => folderModules.TryGetValue(Folder(d), out var m) ? m : null)
        .Distinct()
        .ToList();

    if (modules.Count == 1 && modules[0] != null)
    {
        owners[name] = modules[0]!;
    }
    else
    {
        Console.Error.WriteLine($"cannot map {name} to a module (documents: {string.Join(", ", Documents(handle))})");
        failed = true;
    }
}

if (failed)
{
    return 1;
}

foreach (var (name, module) in owners)
{
    Console.WriteLine($"{name}\t{module}");
}

return 0;

static string FullName(MetadataReader reader, TypeDefinition type)
{
    var ns = reader.GetString(type.Namespace);
    var name = reader.GetString(type.Name);
    return ns.Length == 0 ? name : ns + "." + name;
}

(string? Module, bool DeclaresModule) OwningModule(TypeDefinition type)
{
    foreach (var attributeHandle in type.GetCustomAttributes())
    {
        var attribute = stdlib.GetCustomAttribute(attributeHandle);
        var attributeName = AttributeTypeName(attribute);
        if (attributeName != "SharpyModuleAttribute" && attributeName != "SharpyModuleTypeAttribute")
        {
            continue;
        }

        // Custom attribute blob: prolog (0x0001), then the first fixed argument.
        var blob = stdlib.GetBlobReader(attribute.Value);
        blob.ReadUInt16();
        return (blob.ReadSerializedString(), attributeName == "SharpyModuleAttribute");
    }

    return (null, false);
}

string AttributeTypeName(CustomAttribute attribute)
{
    var parent = attribute.Constructor.Kind == HandleKind.MemberReference
        ? stdlib.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent
        : stdlib.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType();

    return parent.Kind switch
    {
        HandleKind.TypeReference => stdlib.GetString(stdlib.GetTypeReference((TypeReferenceHandle)parent).Name),
        HandleKind.TypeDefinition => stdlib.GetString(stdlib.GetTypeDefinition((TypeDefinitionHandle)parent).Name),
        _ => string.Empty
    };
}

// The source files a type is declared in: those of its method bodies, or, for a
// type without bodies (an interface), Roslyn's TypeDefinitionDocuments record.
IEnumerable<string> Documents(TypeDefinitionHandle handle)
{
    var documents = new SortedSet<string>(StringComparer.Ordinal);

    foreach (var methodHandle in stdlib.GetTypeDefinition(handle).GetMethods())
    {
        var debug = pdb.GetMethodDebugInformation(methodHandle.ToDebugInformationHandle());
        if (!debug.Document.IsNil)
        {
            documents.Add(DocumentPath(debug.Document));
        }
        else
        {
            foreach (var point in debug.GetSequencePoints())
            {
                documents.Add(DocumentPath(point.Document));
            }
        }
    }

    foreach (var cdiHandle in pdb.GetCustomDebugInformation(handle))
    {
        var cdi = pdb.GetCustomDebugInformation(cdiHandle);
        if (pdb.GetGuid(cdi.Kind) != typeDefinitionDocuments)
        {
            continue;
        }

        var blob = pdb.GetBlobReader(cdi.Value);
        while (blob.RemainingBytes > 0)
        {
            documents.Add(DocumentPath(MetadataTokens.DocumentHandle(blob.ReadCompressedInteger())));
        }
    }

    return documents;
}

string DocumentPath(DocumentHandle handle)
{
    return pdb.GetString(pdb.GetDocument(handle).Name).Replace('\\', '/');
}

static string Folder(string documentPath)
{
    return Path.GetDirectoryName(documentPath)?.Replace('\\', '/') ?? string.Empty;
}
