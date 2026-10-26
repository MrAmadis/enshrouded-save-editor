using System.Text;
using EnshroudedSaveEditor.GameData.Kfc;

namespace EnshroudedSaveEditor.Tests;

/// <summary>
/// Type metadata crafted in memory, the way misread metadata from a patched executable could look. A cycle must give
/// <see cref="InvalidDataException"/> (the reference-data fallback), never a hang or a stack overflow.
/// </summary>
public class KfcTypesTests
{
    static KfcType Type(string name, KfcKind kind, int size = 0, int count = 0) =>
        new(name, name, kind, size, count, KfcTypes.Fnv1a(Encoding.ASCII.GetBytes(name)));

    /// <summary>A struct with one field, <paramref name="field"/>, at offset 0.</summary>
    static KfcType Wrapper(string name, KfcType field)
    {
        var s = Type(name, KfcKind.Struct, field.Size, 1);
        s.Fields = [new KfcField("value", field, 0)];
        return s;
    }

    [Fact]
    public void A_typedef_chain_that_comes_back_round_is_rejected()
    {
        var a = Type("A", KfcKind.Typedef);
        var b = Type("B", KfcKind.Typedef);
        a.Inner = b;
        b.Inner = a;
        Assert.Throws<InvalidDataException>(() => new KfcTypes([a, b]));

        var self = Type("Self", KfcKind.Typedef);
        self.Inner = self;
        Assert.Throws<InvalidDataException>(() => new KfcTypes([self]));
    }

    [Fact]
    public void A_base_struct_chain_that_comes_back_round_is_rejected()
    {
        var a = Type("A", KfcKind.Struct);
        var b = Type("B", KfcKind.Struct);
        var c = Type("C", KfcKind.Typedef);
        a.Inner = b;
        b.Inner = c;
        c.Inner = a;
        Assert.Throws<InvalidDataException>(() => new KfcTypes([a, b, c]));
    }

    [Fact]
    public void A_struct_may_refer_to_itself_through_an_array()
    {
        // A node based on a blob array of nodes: the chain goes on through the array, which no lookup follows.
        var node = Type("Node", KfcKind.Struct, 8);
        var nodes = Type("BlobArray<Node>", KfcKind.BlobArray, 8);
        node.Inner = nodes;
        nodes.Inner = node;

        var types = new KfcTypes([node, nodes]);

        Assert.Same(node, types.Find(node.Hash)!.Resolved);
        Assert.Null(node.Field("missing"));
    }

    [Fact]
    public void Numbers_are_read_through_single_field_structs()
    {
        var u32 = Type("uint32", KfcKind.UInt32, 4);
        var f32 = Type("float32", KfcKind.Float32, 4);
        var id = Wrapper("Id", u32);
        var wrapped = Wrapper("Wrapped", id);
        var real = Wrapper("Real", f32);
        var types = new KfcTypes([u32, f32, id, wrapped, real]);

        Assert.Equal(7, new KfcValue(types, wrapped, [7, 0, 0, 0]).Int);
        Assert.Equal(1.5, new KfcValue(types, real, BitConverter.GetBytes(1.5f)).Float);
    }

    [Fact]
    public void Numbers_wrapped_too_deep_are_rejected()
    {
        var u32 = Type("uint32", KfcKind.UInt32, 4);
        var f32 = Type("float32", KfcKind.Float32, 4);
        var all = new List<KfcType> { u32, f32 };
        KfcType deepInt = u32, deepFloat = f32;
        for (var i = 0; i < 100; i++)
        {
            all.Add(deepInt = Wrapper($"I{i}", deepInt));
            all.Add(deepFloat = Wrapper($"F{i}", deepFloat));
        }
        var types = new KfcTypes(all);

        Assert.Throws<InvalidDataException>(() => new KfcValue(types, deepInt, [7, 0, 0, 0]).Int);
        Assert.Throws<InvalidDataException>(() => new KfcValue(types, deepFloat, BitConverter.GetBytes(1.5f)).Float);
    }

    // Before the cap this overflowed the stack, which no catch stops (it would end this test run too).
    [Fact]
    public void A_struct_whose_one_field_is_itself_is_rejected_when_read()
    {
        var loop = Type("Loop", KfcKind.Struct, 4, 1);
        loop.Fields = [new KfcField("value", loop, 0)];
        var value = new KfcValue(new KfcTypes([loop]), loop, [7, 0, 0, 0]);

        Assert.Throws<InvalidDataException>(() => value.Int);
        Assert.Throws<InvalidDataException>(() => value.Float);
    }
}
