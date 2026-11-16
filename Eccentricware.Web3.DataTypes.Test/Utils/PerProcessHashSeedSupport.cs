using EccentricWare.Web3.DataTypes.Utils;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;

namespace EccentricWare.Web3.DataTypes.Tests;

/// <summary>
/// Loads extra copies of the library into collectible <see cref="AssemblyLoadContext"/>s. Each copy runs its own static
/// initialisers, so it draws its own per-process hash seeds and stands in for a second process. Index 0 is the copy the
/// tests themselves use.
/// </summary>
internal sealed class IsolatedLibraryCopies : IDisposable
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    private readonly List<AssemblyLoadContext> _contexts = [];

    /// <summary>The default copy followed by the isolated copies.</summary>
    public Assembly[] Assemblies { get; }

    public IsolatedLibraryCopies(int isolatedCopies = 2)
    {
        Assembly own = typeof(Address).Assembly;
        var all = new List<Assembly> { own };
        for (int i = 0; i < isolatedCopies; i++)
        {
            var context = new AssemblyLoadContext($"hash-seed-isolated-{i}", isCollectible: true);
            _contexts.Add(context);
            Assembly copy = context.LoadFromAssemblyPath(own.Location);
            Assert.AreNotSame(own, copy);
            all.Add(copy);
        }

        Assemblies = [.. all];
    }

    public Type TypeIn(int copy, Type libraryType)
    {
        Type type = Assemblies[copy].GetType(libraryType.FullName!, throwOnError: true)!;
        if (copy != 0)
            Assert.AreNotSame(libraryType, type);
        return type;
    }

    /// <summary>Reads a private static <see cref="ulong"/> field from every copy.</summary>
    public ulong[] ReadStaticFromEveryCopy(Type libraryType, string field)
        => Enumerable.Range(0, Assemblies.Length)
            .Select(i => (ulong)TypeIn(i, libraryType).GetField(field, PrivateStatic)!.GetValue(null)!)
            .ToArray();

    /// <summary>Hash codes of <c>new T(key)</c> for every key, where T has a public single-argument constructor.</summary>
    public int[][] HashCodesFromEveryCopy(Type libraryType, IReadOnlyList<object> keys)
        => Enumerable.Range(0, Assemblies.Length)
            .Select(i =>
            {
                Type type = TypeIn(i, libraryType);
                return keys.Select(key => Activator.CreateInstance(type, key)!.GetHashCode()).ToArray();
            })
            .ToArray();

    /// <summary>
    /// Builds an <see cref="AddressSet"/> from the same EVM addresses in every copy and returns each copy's table of stored
    /// hashes (0 = empty slot). The slot of an entry is its stored hash masked to the table size, so equal tables mean
    /// equal probe sequences.
    /// </summary>
    public ulong[][] AddressSetTablesFromEveryCopy(IReadOnlyList<string> evmHex)
        => Enumerable.Range(0, Assemblies.Length)
            .Select(i =>
            {
                Type addressType = TypeIn(i, typeof(Address));
                Type setType = TypeIn(i, typeof(AddressSet));
                MethodInfo parse = addressType.GetMethod("Parse", [typeof(string)])!;
                Array addresses = Array.CreateInstance(addressType, evmHex.Count);
                for (int k = 0; k < evmHex.Count; k++)
                    addresses.SetValue(parse.Invoke(null, [evmHex[k]]), k);

                object set = BuildSetFromArray(addressType, setType)(addresses);
                return (ulong[])setType.GetField("_hashes", PrivateInstance)!.GetValue(set)!;
            })
            .ToArray();

    // MethodInfo.Invoke cannot pass a ReadOnlySpan, so emit: (Array a) => AddressSet.Build(new ReadOnlySpan<Address>((Address[])a), 0.72).
    private static Func<Array, object> BuildSetFromArray(Type addressType, Type setType)
    {
        Type spanType = typeof(ReadOnlySpan<>).MakeGenericType(addressType);
        MethodInfo build = setType.GetMethod("Build", [spanType, typeof(double)])!;
        ConstructorInfo spanFromArray = spanType.GetConstructor([addressType.MakeArrayType()])!;
        var method = new DynamicMethod("BuildAddressSet", typeof(object), [typeof(Array)], typeof(IsolatedLibraryCopies).Module, skipVisibility: true);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, addressType.MakeArrayType());
        il.Emit(OpCodes.Newobj, spanFromArray);
        il.Emit(OpCodes.Ldc_R8, 0.72);
        il.Emit(OpCodes.Call, build);
        il.Emit(OpCodes.Ret);
        return (Func<Array, object>)method.CreateDelegate(typeof(Func<Array, object>));
    }

    public void Dispose()
    {
        foreach (AssemblyLoadContext context in _contexts)
            context.Unload();
    }
}
