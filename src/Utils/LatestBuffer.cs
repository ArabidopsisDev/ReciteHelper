namespace ReciteHelper.Utils;

public class LatestBuffer<T> where T : struct
{
    private T?[] _internalArray;
    private int _pivot = 0;

    public LatestBuffer(int size)
    {
        _internalArray = new T?[size + 1];

        for (int i = 0; i < size; i++) _internalArray[i] = null;
    }

    public void Add(T value)
    {
        _internalArray[_pivot] = value;
        _pivot++;

        if (_pivot > _internalArray.Length - 1)
        {
            for (int i = 0; i < _internalArray.Length - 1; i++)
                _internalArray[i] = _internalArray[i + 1];
            _pivot--;
        }
    }

    public void Clear()
    {
        _pivot = 0;
    }

    public bool EqualsTo(T value)
    {
        for (int i = 0; i < _internalArray.Length - 1; i++)
        {
            if (!_internalArray[i].Equals(value))
                return false;
        }

        return true;
    }

    public void Println()
    {
        for (int i = 0; i < _internalArray.Length - 1; i++)
            Console.WriteLine(_internalArray[i]);
    }
}