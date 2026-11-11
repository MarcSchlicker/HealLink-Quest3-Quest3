using System;
using System.IO;

internal static class HeadsetFrameProtocol
{
    internal const int MaxFrameBytes = 512 * 1024;
    private static readonly byte[] Magic = { (byte)'H', (byte)'Q', (byte)'V', (byte)'1' };

    internal static void WriteHello(Stream stream) => stream.Write(Magic, 0, Magic.Length);

    internal static void ReadHello(Stream stream)
    {
        byte[] hello = ReadExactly(stream, Magic.Length);
        for (int i = 0; i < Magic.Length; i++)
            if (hello[i] != Magic[i]) throw new InvalidDataException("Unsupported headset view protocol");
    }

    internal static void WriteFrame(Stream stream, byte[] frame)
    {
        int length = frame?.Length ?? 0;
        if (length > MaxFrameBytes || (length != 0 && !IsJpeg(frame)))
            throw new InvalidDataException("Invalid headset frame");
        var header = new byte[] { (byte)(length >> 24), (byte)(length >> 16),
            (byte)(length >> 8), (byte)length };
        stream.Write(header, 0, header.Length);
        if (length > 0) stream.Write(frame, 0, length);
    }

    internal static byte[] ReadFrame(Stream stream)
    {
        byte[] header = ReadExactly(stream, 4);
        uint length = ((uint)header[0] << 24) | ((uint)header[1] << 16) |
            ((uint)header[2] << 8) | header[3];
        if (length > MaxFrameBytes) throw new InvalidDataException("Headset frame exceeds size limit");
        if (length == 0) return null;
        byte[] frame = ReadExactly(stream, (int)length);
        if (!IsJpeg(frame)) throw new InvalidDataException("Expected JPEG headset frame");
        return frame;
    }

    private static bool IsJpeg(byte[] frame) =>
        frame != null && frame.Length >= 4 && frame[0] == 0xff && frame[1] == 0xd8 &&
        frame[frame.Length - 2] == 0xff && frame[frame.Length - 1] == 0xd9;

    private static byte[] ReadExactly(Stream stream, int length)
    {
        var bytes = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = stream.Read(bytes, offset, length - offset);
            if (read == 0) throw new EndOfStreamException("Headset stream closed");
            offset += read;
        }
        return bytes;
    }
}
