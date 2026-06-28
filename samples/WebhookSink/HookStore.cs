namespace WebhookSink;

internal interface IHookStore
{
    void Add(HookCapture hook);
    IReadOnlyList<HookCapture> GetAll();
    HookCapture? GetById(Guid id);
}

internal sealed class HookStore : IHookStore
{
    private readonly int _capacity;
    private readonly Queue<HookCapture> _ring;
    private readonly Dictionary<Guid, HookCapture> _index;
    private readonly Lock _lock = new();

    public HookStore(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _ring = new Queue<HookCapture>(capacity);
        _index = new Dictionary<Guid, HookCapture>(capacity);
    }

    public void Add(HookCapture hook)
    {
        lock (_lock)
        {
            if (_ring.Count >= _capacity)
            {
                var evicted = _ring.Dequeue();
                _index.Remove(evicted.Id);
            }
            _ring.Enqueue(hook);
            _index[hook.Id] = hook;
        }
    }

    public IReadOnlyList<HookCapture> GetAll()
    {
        lock (_lock)
        {
            return _ring.Reverse().ToArray();
        }
    }

    public HookCapture? GetById(Guid id)
    {
        lock (_lock)
        {
            return _index.GetValueOrDefault(id);
        }
    }
}
