﻿using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BTDB.Buffer;

static class TreeNodeUtils
{
    internal static void ThrowCursorHaveToBeValid()
    {
        throw new InvalidOperationException("Cursor must be valid for this operation");
    }

    internal static void ThrowCursorNotWritable()
    {
        throw new InvalidOperationException("Cursor not writable");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void AssertLittleEndian()
    {
        if (!BitConverter.IsLittleEndian)
        {
            throw new NotSupportedException("Only Little Endian platform supported");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static unsafe void WriteInt32Aligned(IntPtr ptr, int value)
    {
        *(int*)ptr = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static unsafe int ReadInt32Aligned(IntPtr ptr)
    {
        return *(int*)ptr;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static IntPtr AlignPtrUpInt16(IntPtr ptr)
    {
        return (IntPtr)(ptr.ToInt64() + 1 & ~1L);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static uint AlignUIntUpInt16(uint ptr)
    {
        return ptr + 1u & ~1u;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static IntPtr AlignPtrUpInt32(IntPtr ptr)
    {
        return (IntPtr)(ptr.ToInt64() + 3 & ~3L);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static uint AlignUIntUpInt32(uint ptr)
    {
        return ptr + 3u & ~3u;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static IntPtr AlignPtrUpInt64(IntPtr ptr)
    {
        return (IntPtr)(ptr.ToInt64() + 7 & ~7L);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static uint AlignUIntUpInt64(uint ptr)
    {
        return ptr + 7u & ~7u;
    }

    internal static unsafe void CopyMemory(IntPtr src, IntPtr dst, int size)
    {
        Unsafe.CopyBlockUnaligned(dst.ToPointer(), src.ToPointer(), (uint)size);
    }

    internal static bool IsPrefix(in ReadOnlySpan<byte> data, in ReadOnlySpan<byte> prefix)
    {
        if (data.Length < prefix.Length)
            return false;
        return data.Slice(0, prefix.Length).SequenceEqual(prefix);
    }

    internal static unsafe int FindFirstDifference(in ReadOnlySpan<byte> buf1, in ReadOnlySpan<byte> buf2)
    {
        var len = Math.Min(buf1.Length, buf2.Length);
        if (len == 0) return 0;
        fixed (byte* buf1Ptr = &MemoryMarshal.GetReference(buf1))
        fixed (byte* buf2Ptr = &MemoryMarshal.GetReference(buf2))
        {
            var i = 0;
            int n;
            if (Vector.IsHardwareAccelerated && len >= Vector<byte>.Count)
            {
                n = len - Vector<byte>.Count;
                while (n >= i)
                {
                    if (Unsafe.ReadUnaligned<Vector<byte>>(buf1Ptr + i) !=
                        Unsafe.ReadUnaligned<Vector<byte>>(buf2Ptr + i))
                        break;
                    i += Vector<byte>.Count;
                }
            }

            n = len - sizeof(long);
            while (n >= i)
            {
                if (Unsafe.ReadUnaligned<long>(buf1Ptr + i) != Unsafe.ReadUnaligned<long>(buf2Ptr + i))
                    break;
                i += sizeof(long);
            }

            while (len > i)
            {
                if (*(buf1Ptr + i) != *(buf2Ptr + i))
                    break;
                i++;
            }

            return i;
        }
    }

    internal static int FindFirstDifference(in ReadOnlySpan<byte> buf1, in ReadOnlySpan<byte> buf2a,
        in ReadOnlySpan<byte> buf2b)
    {
        var pos = FindFirstDifference(buf1, buf2a);
        if (pos < buf2a.Length || pos == buf1.Length)
        {
            return pos;
        }

        return buf2a.Length + FindFirstDifference(buf1.Slice(buf2a.Length), buf2b);
    }

    public static int FindFirstDifference(in Span<byte> buf1a, in Span<byte> buf1b, in Span<byte> buf2a,
        in Span<byte> buf2b)
    {
        var pos = FindFirstDifference(buf1a, buf2a, buf2b);
        if (pos < buf1a.Length)
            return pos;
        if (pos < buf2a.Length) return pos + FindFirstDifference(buf1b, buf2a.Slice(pos), buf2b);
        if (pos == buf2a.Length + buf2b.Length)
        {
            return pos;
        }

        return pos + FindFirstDifference(buf1b, buf2b.Slice(pos - buf2a.Length));
    }

    internal static uint CalcCommonPrefix(in Span<ByteBuffer> keys)
    {
        var first = keys[0].AsSyncReadOnlySpan();
        var res = first.Length;
        for (var i = 1; i < keys.Length; i++)
        {
            res = FindFirstDifference(first, keys[i].AsSyncReadOnlySpan());
            if (res == 0) return 0;
            first = first.Slice(0, res);
        }

        return (uint)res;
    }
}
