using System;
using System.Collections.Generic;

namespace BTDB.KVDBLayer.BTree;

class BTreeLeaf : IBTreeLeafNode, IBTreeNode
{
    internal readonly long TransactionId;
    BTreeLeafMember[] _keyValues;
    internal const int MaxMembers = 30;

    BTreeLeaf(long transactionId, int length)
    {
        TransactionId = transactionId;
        _keyValues = new BTreeLeafMember[length];
    }

    internal BTreeLeaf(long transactionId, BTreeLeafMember[] newKeyValues)
    {
        TransactionId = transactionId;
        _keyValues = newKeyValues;
    }

    internal static IBTreeNode CreateFirst(ref CreateOrUpdateCtx ctx)
    {
        var result = new BTreeLeaf(ctx.TransactionId, 1);
        result._keyValues[0] = NewMemberFromCtx(ref ctx);
        return result;
    }

    int Find(in ReadOnlySpan<byte> key)
    {
        var left = 0;
        var right = _keyValues.Length;
        while (left < right)
        {
            var middle = (left + right) / 2;
            var currentKey = _keyValues[middle].Key;
            var result = key.SequenceCompareTo(currentKey);
            if (result == 0)
            {
                return middle * 2 + 1;
            }

            if (result < 0)
            {
                right = middle;
            }
            else
            {
                left = middle + 1;
            }
        }

        return left * 2;
    }

    public void CreateOrUpdate(ref CreateOrUpdateCtx ctx)
    {
        var index = Find(ctx.Key);
        if ((index & 1) == 1)
        {
            index = (int)((uint)index / 2);
            ctx.Created = false;
            ctx.KeyIndex = index;
            var m = _keyValues[index];
            m.ValueFileId = ctx.ValueFileId;
            m.ValueOfs = ctx.ValueOfs;
            m.ValueSize = ctx.ValueSize;
            var leaf = this;
            if (ctx.TransactionId != TransactionId)
            {
                leaf = new BTreeLeaf(ctx.TransactionId, _keyValues.Length);
                Array.Copy(_keyValues, leaf._keyValues, _keyValues.Length);
                ctx.Node1 = leaf;
                ctx.Update = true;
            }

            leaf._keyValues[index] = m;
            ctx.Stack!.Add(new NodeIdxPair { Node = leaf, Idx = index });
            return;
        }

        index = (int)((uint)index / 2);
        ctx.Created = true;
        ctx.KeyIndex = index;
        if (_keyValues.Length < MaxMembers)
        {
            var newKeyValues = new BTreeLeafMember[_keyValues.Length + 1];
            Array.Copy(_keyValues, 0, newKeyValues, 0, index);
            newKeyValues[index] = NewMemberFromCtx(ref ctx);
            Array.Copy(_keyValues, index, newKeyValues, index + 1, _keyValues.Length - index);
            var leaf = this;
            if (ctx.TransactionId != TransactionId)
            {
                leaf = new BTreeLeaf(ctx.TransactionId, newKeyValues);
                ctx.Node1 = leaf;
                ctx.Update = true;
            }
            else
            {
                _keyValues = newKeyValues;
            }

            ctx.Stack!.Add(new NodeIdxPair { Node = leaf, Idx = index });
            return;
        }

        ctx.Split = true;
        var keyCountLeft = (_keyValues.Length + 1) / 2;
        var keyCountRight = _keyValues.Length + 1 - keyCountLeft;
        var leftNode = new BTreeLeaf(ctx.TransactionId, keyCountLeft);
        var rightNode = new BTreeLeaf(ctx.TransactionId, keyCountRight);
        ctx.Node1 = leftNode;
        ctx.Node2 = rightNode;
        if (index < keyCountLeft)
        {
            Array.Copy(_keyValues, 0, leftNode._keyValues, 0, index);
            leftNode._keyValues[index] = NewMemberFromCtx(ref ctx);
            Array.Copy(_keyValues, index, leftNode._keyValues, index + 1, keyCountLeft - index - 1);
            Array.Copy(_keyValues, keyCountLeft - 1, rightNode._keyValues, 0, keyCountRight);
            ctx.Stack!.Add(new NodeIdxPair { Node = leftNode, Idx = index });
            ctx.SplitInRight = false;
        }
        else
        {
            Array.Copy(_keyValues, 0, leftNode._keyValues, 0, keyCountLeft);
            Array.Copy(_keyValues, keyCountLeft, rightNode._keyValues, 0, index - keyCountLeft);
            rightNode._keyValues[index - keyCountLeft] = NewMemberFromCtx(ref ctx);
            Array.Copy(_keyValues, index, rightNode._keyValues, index - keyCountLeft + 1,
                keyCountLeft + keyCountRight - 1 - index);
            ctx.Stack!.Add(new NodeIdxPair { Node = rightNode, Idx = index - keyCountLeft });
            ctx.SplitInRight = true;
        }
    }

    public FindResult FindKey(List<NodeIdxPair> stack, out long keyIndex, in ReadOnlySpan<byte> key)
    {
        var idx = Find(key);
        FindResult result;
        if ((idx & 1) == 1)
        {
            result = FindResult.Exact;
            idx = (int)((uint)idx / 2);
        }
        else
        {
            result = FindResult.Previous;
            idx = (int)((uint)idx / 2) - 1;
        }

        stack.Add(new NodeIdxPair { Node = this, Idx = idx });
        keyIndex = idx;
        return result;
    }

    static BTreeLeafMember NewMemberFromCtx(ref CreateOrUpdateCtx ctx)
    {
        return new BTreeLeafMember
        {
            Key = ctx.Key.ToArray(),
            ValueFileId = ctx.ValueFileId,
            ValueOfs = ctx.ValueOfs,
            ValueSize = ctx.ValueSize
        };
    }

    public long CalcKeyCount()
    {
        return _keyValues.Length;
    }

    public byte[] GetLeftMostKey()
    {
        return _keyValues[0].Key;
    }

    public void FillStackByIndex(List<NodeIdxPair> stack, long keyIndex)
    {
        stack.Add(new NodeIdxPair { Node = this, Idx = (int)keyIndex });
    }

    public long FindLastWithPrefix(in ReadOnlySpan<byte> prefix)
    {
        var left = 0;
        var right = _keyValues.Length - 1;
        byte[] currentKey;
        int result;
        while (left < right)
        {
            var middle = (left + right) / 2;
            currentKey = _keyValues[middle].Key;
            result = prefix.SequenceCompareTo(currentKey.AsSpan(0, Math.Min(currentKey.Length, prefix.Length)));
            if (result < 0)
            {
                right = middle;
            }
            else
            {
                left = middle + 1;
            }
        }

        currentKey = _keyValues[left].Key;
        result = prefix.SequenceCompareTo(currentKey.AsSpan(0, Math.Min(currentKey.Length, prefix.Length)));
        if (result < 0) left--;
        return left;
    }

    public bool NextIdxValid(int idx)
    {
        return idx + 1 < _keyValues.Length;
    }

    public void FillStackByLeftMost(List<NodeIdxPair> stack, int idx)
    {
        // Nothing to do
    }

    public void FillStackByRightMost(List<NodeIdxPair> stack, int i)
    {
        // Nothing to do
    }

    public int GetLastChildrenIdx()
    {
        return _keyValues.Length - 1;
    }

    public IBTreeNode EraseRange(long transactionId, long firstKeyIndex, long lastKeyIndex)
    {
        var newKeyValues = new BTreeLeafMember[_keyValues.Length + firstKeyIndex - lastKeyIndex - 1];
        Array.Copy(_keyValues, 0, newKeyValues, 0, (int)firstKeyIndex);
        Array.Copy(_keyValues, (int)lastKeyIndex + 1, newKeyValues, (int)firstKeyIndex,
            newKeyValues.Length - (int)firstKeyIndex);
        if (TransactionId == transactionId)
        {
            _keyValues = newKeyValues;
            return this;
        }

        return new BTreeLeaf(transactionId, newKeyValues);
    }

    public IBTreeNode EraseOne(long transactionId, long keyIndex)
    {
        var newKeyValues = new BTreeLeafMember[_keyValues.Length - 1];
        Array.Copy(_keyValues, 0, newKeyValues, 0, (int)keyIndex);
        Array.Copy(_keyValues, (int)keyIndex + 1, newKeyValues, (int)keyIndex,
            newKeyValues.Length - (int)keyIndex);
        if (TransactionId == transactionId)
        {
            _keyValues = newKeyValues;
            return this;
        }

        return new BTreeLeaf(transactionId, newKeyValues);
    }

    public void Iterate(ValuesIterateAction action)
    {
        var kv = _keyValues;
        foreach (var member in kv)
        {
            if (member.ValueFileId == 0) continue;
            action(member.ValueFileId, member.ValueOfs, member.ValueSize);
        }
    }

    public IBTreeNode ReplaceValues(ReplaceValuesCtx ctx)
    {
        var result = this;
        var keyValues = _keyValues;
        var map = ctx._newPositionMap;
        for (var i = 0; i < keyValues.Length; i++)
        {
            ref var ii = ref keyValues[i];
            if (map.TryGetValue(((ulong)ii.ValueFileId << 32) | ii.ValueOfs, out var newOffset))
            {
                if (result.TransactionId != ctx._transactionId)
                {
                    var newKeyValues = new BTreeLeafMember[keyValues.Length];
                    Array.Copy(keyValues, newKeyValues, newKeyValues.Length);
                    result = new BTreeLeaf(ctx._transactionId, newKeyValues);
                    keyValues = newKeyValues;
                }

                keyValues[i].ValueFileId = (uint)(newOffset >> 32);
                keyValues[i].ValueOfs = (uint)newOffset;
            }
        }

        return result;
    }

    public ReadOnlySpan<byte> GetKey(int idx)
    {
        return _keyValues[idx].Key;
    }

    public BTreeValue GetMemberValue(int idx)
    {
        var kv = _keyValues[idx];
        return new BTreeValue
        {
            ValueFileId = kv.ValueFileId,
            ValueOfs = kv.ValueOfs,
            ValueSize = kv.ValueSize
        };
    }

    public void SetMemberValue(int idx, in BTreeValue value)
    {
        ref var kv = ref _keyValues[idx];
        kv.ValueFileId = value.ValueFileId;
        kv.ValueOfs = value.ValueOfs;
        kv.ValueSize = value.ValueSize;
    }
}
