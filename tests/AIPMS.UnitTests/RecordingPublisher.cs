using MediatR;

namespace AIPMS.UnitTests;

internal sealed class RecordingPublisher(Action? onPublish = null) : IPublisher
{
    public List<object> Events { get; } = [];
    public Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        onPublish?.Invoke();
        Events.Add(notification);
        return Task.CompletedTask;
    }
    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification => Publish((object)notification, cancellationToken);
}
