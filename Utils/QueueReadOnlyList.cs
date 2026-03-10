using System;
using System.Collections.Generic;

namespace MTFVoiceTools.Utils;

public class QueueReadOnlyList<T> : Queue<T>, IReadOnlyList<T>
{
    public T this[int index] => GetElementAt(index);

    private readonly Func<T[]>  _getArray;
    private readonly Func<int> _getHead;
    
    public QueueReadOnlyList()
    {
        Func<Queue<T>, int> getHead = FastField<Queue<T>, int>.CreateGetter("_head");
        _getHead = () => getHead(this);
        Func<Queue<T>, T[]> getArray = FastField<Queue<T>, T[]>.CreateGetter("_array");
        _getArray = () => getArray(this);
    }
    
    private T GetElementAt(int index)
    {
        if (index < 0 || index >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        T[] array = _getArray();
        int head = _getHead();
        int actualIndex = (head + index) % array.Length;

        return array[actualIndex];
    }
}