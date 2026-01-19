namespace AntiBridge.Core.Services;

/// <summary>
/// Protobuf encoding/decoding utilities for Antigravity's state format.
/// Ported from Antigravity-Manager protobuf.rs
/// </summary>
public static class ProtobufHelper
{
    /// <summary>
    /// Encode a value as a Protobuf varint
    /// </summary>
    public static byte[] EncodeVarint(ulong value)
    {
        var result = new List<byte>();
        while (value >= 0x80)
        {
            result.Add((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        result.Add((byte)value);
        return result.ToArray();
    }

    /// <summary>
    /// Read a varint from data at offset, returns (value, newOffset)
    /// </summary>
    public static (ulong Value, int NewOffset) ReadVarint(byte[] data, int offset)
    {
        ulong result = 0;
        int shift = 0;
        int pos = offset;

        while (pos < data.Length)
        {
            byte b = data[pos];
            result |= (ulong)(b & 0x7F) << shift;
            pos++;
            if ((b & 0x80) == 0)
                break;
            shift += 7;
        }

        return (result, pos);
    }

    /// <summary>
    /// Skip a protobuf field based on wire type
    /// </summary>
    public static int SkipField(byte[] data, int offset, int wireType)
    {
        switch (wireType)
        {
            case 0: // Varint
                var (_, newOffset) = ReadVarint(data, offset);
                return newOffset;
            case 1: // 64-bit
                return offset + 8;
            case 2: // Length-delimited
                var (length, contentOffset) = ReadVarint(data, offset);
                return contentOffset + (int)length;
            case 5: // 32-bit
                return offset + 4;
            default:
                throw new Exception($"Unknown wire type: {wireType}");
        }
    }

    /// <summary>
    /// Find a specific protobuf field (length-delimited) and return its content
    /// </summary>
    public static byte[]? FindField(byte[] data, int targetField)
    {
        int offset = 0;
        while (offset < data.Length)
        {
            try
            {
                var (tag, newOffset) = ReadVarint(data, offset);
                int wireType = (int)(tag & 7);
                int fieldNum = (int)(tag >> 3);

                if (fieldNum == targetField && wireType == 2)
                {
                    var (length, contentOffset) = ReadVarint(data, newOffset);
                    var result = new byte[(int)length];
                    Array.Copy(data, contentOffset, result, 0, (int)length);
                    return result;
                }

                offset = SkipField(data, newOffset, wireType);
            }
            catch
            {
                break;
            }
        }
        return null;
    }

    /// <summary>
    /// Remove a specific field from protobuf data
    /// </summary>
    public static byte[] RemoveField(byte[] data, int fieldToRemove)
    {
        var result = new List<byte>();
        int offset = 0;

        while (offset < data.Length)
        {
            int startOffset = offset;
            var (tag, newOffset) = ReadVarint(data, offset);
            int wireType = (int)(tag & 7);
            int fieldNum = (int)(tag >> 3);
            int nextOffset = SkipField(data, newOffset, wireType);

            if (fieldNum != fieldToRemove)
            {
                // Keep this field
                for (int i = startOffset; i < nextOffset; i++)
                    result.Add(data[i]);
            }

            offset = nextOffset;
        }

        return result.ToArray();
    }

    /// <summary>
    /// Create OAuth Field 6 for injection into Antigravity state
    /// Structure: access_token (1), token_type (2), refresh_token (3), expiry (4)
    /// </summary>
    public static byte[] CreateOAuthField(string accessToken, string refreshToken, long expiryTimestamp)
    {
        // Field 1: access_token
        var field1 = CreateStringField(1, accessToken);

        // Field 2: token_type = "Bearer"
        var field2 = CreateStringField(2, "Bearer");

        // Field 3: refresh_token
        var field3 = CreateStringField(3, refreshToken);

        // Field 4: expiry (nested Timestamp message with Field 1 = seconds)
        var timestampInner = new List<byte>();
        timestampInner.AddRange(EncodeVarint((1 << 3) | 0)); // Field 1, varint
        timestampInner.AddRange(EncodeVarint((ulong)expiryTimestamp));
        var field4 = CreateBytesField(4, timestampInner.ToArray());

        // Combine all fields into OAuthTokenInfo message
        var oauthInfo = new List<byte>();
        oauthInfo.AddRange(field1);
        oauthInfo.AddRange(field2);
        oauthInfo.AddRange(field3);
        oauthInfo.AddRange(field4);

        // Wrap as Field 6 (length-delimited)
        return CreateBytesField(6, oauthInfo.ToArray());
    }

    private static byte[] CreateStringField(int fieldNum, string value)
    {
        var result = new List<byte>();
        result.AddRange(EncodeVarint((ulong)((fieldNum << 3) | 2))); // wire_type = 2
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        result.AddRange(EncodeVarint((ulong)bytes.Length));
        result.AddRange(bytes);
        return result.ToArray();
    }

    private static byte[] CreateBytesField(int fieldNum, byte[] value)
    {
        var result = new List<byte>();
        result.AddRange(EncodeVarint((ulong)((fieldNum << 3) | 2))); // wire_type = 2
        result.AddRange(EncodeVarint((ulong)value.Length));
        result.AddRange(value);
        return result.ToArray();
    }
}
