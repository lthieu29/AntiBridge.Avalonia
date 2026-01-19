using NUnit.Framework;
using AntiBridge.Core.Services;

namespace AntiBridge.Tests;

[TestFixture]
public class ProtobufHelperTests
{
    [Test]
    public void EncodeVarint_SmallValue_ReturnsOneByte()
    {
        var result = ProtobufHelper.EncodeVarint(127);
        Assert.That(result.Length, Is.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(127));
    }

    [Test]
    public void EncodeVarint_LargeValue_ReturnsMultipleBytes()
    {
        var result = ProtobufHelper.EncodeVarint(300);
        Assert.That(result.Length, Is.EqualTo(2));
        // 300 = 0b100101100 -> varint: [0xAC, 0x02]
        Assert.That(result[0], Is.EqualTo(0xAC));
        Assert.That(result[1], Is.EqualTo(0x02));
    }

    [Test]
    public void ReadVarint_SingleByte_ReturnsCorrectValue()
    {
        byte[] data = [50];
        var (value, offset) = ProtobufHelper.ReadVarint(data, 0);
        Assert.That(value, Is.EqualTo(50));
        Assert.That(offset, Is.EqualTo(1));
    }

    [Test]
    public void ReadVarint_MultiByte_ReturnsCorrectValue()
    {
        byte[] data = [0xAC, 0x02]; // 300
        var (value, offset) = ProtobufHelper.ReadVarint(data, 0);
        Assert.That(value, Is.EqualTo(300));
        Assert.That(offset, Is.EqualTo(2));
    }

    [Test]
    public void EncodeAndReadVarint_RoundTrip_PreservesValue()
    {
        ulong original = 123456789;
        var encoded = ProtobufHelper.EncodeVarint(original);
        var (decoded, _) = ProtobufHelper.ReadVarint(encoded, 0);
        Assert.That(decoded, Is.EqualTo(original));
    }

    [Test]
    public void FindField_ExistingField_ReturnsContent()
    {
        // Create a simple protobuf with field 1 = "hello"
        var field = CreateStringField(1, "hello");
        var result = ProtobufHelper.FindField(field, 1);
        
        Assert.That(result, Is.Not.Null);
        Assert.That(System.Text.Encoding.UTF8.GetString(result!), Is.EqualTo("hello"));
    }

    [Test]
    public void FindField_NonExistingField_ReturnsNull()
    {
        var field = CreateStringField(1, "hello");
        var result = ProtobufHelper.FindField(field, 99);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void RemoveField_ExistingField_RemovesIt()
    {
        // Create data with field 1 and field 2
        var field1 = CreateStringField(1, "one");
        var field2 = CreateStringField(2, "two");
        var combined = field1.Concat(field2).ToArray();

        var result = ProtobufHelper.RemoveField(combined, 1);

        // Field 1 should be gone, field 2 should remain
        var field1Content = ProtobufHelper.FindField(result, 1);
        var field2Content = ProtobufHelper.FindField(result, 2);
        
        Assert.That(field1Content, Is.Null);
        Assert.That(field2Content, Is.Not.Null);
        Assert.That(System.Text.Encoding.UTF8.GetString(field2Content!), Is.EqualTo("two"));
    }

    [Test]
    public void CreateOAuthField_ValidInput_CreatesValidProtobuf()
    {
        var result = ProtobufHelper.CreateOAuthField("access123", "refresh456", 1700000000);
        
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Length, Is.GreaterThan(0));
        
        // The outer wrapper is field 6
        var oauthData = ProtobufHelper.FindField(result, 6);
        Assert.That(oauthData, Is.Not.Null);
        
        // Inner fields: 1=access_token, 2=token_type, 3=refresh_token
        var accessToken = ProtobufHelper.FindField(oauthData!, 1);
        var tokenType = ProtobufHelper.FindField(oauthData!, 2);
        var refreshToken = ProtobufHelper.FindField(oauthData!, 3);
        
        Assert.That(System.Text.Encoding.UTF8.GetString(accessToken!), Is.EqualTo("access123"));
        Assert.That(System.Text.Encoding.UTF8.GetString(tokenType!), Is.EqualTo("Bearer"));
        Assert.That(System.Text.Encoding.UTF8.GetString(refreshToken!), Is.EqualTo("refresh456"));
    }

    private static byte[] CreateStringField(int fieldNum, string value)
    {
        var result = new List<byte>();
        result.AddRange(ProtobufHelper.EncodeVarint((ulong)((fieldNum << 3) | 2)));
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        result.AddRange(ProtobufHelper.EncodeVarint((ulong)bytes.Length));
        result.AddRange(bytes);
        return result.ToArray();
    }
}
