using System.Threading.Channels;
using Metagen.Shared.Services;

namespace MetaGrow.Api.Services;

public sealed record OutgoingMail(List<string> To, string Subject, string HtmlBody);

public sealed class MailQueue
{
    private readonly Channel<OutgoingMail> channel = Channel.CreateUnbounded<OutgoingMail>(
        new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(OutgoingMail mail) => channel.Writer.TryWrite(mail);
    public IAsyncEnumerable<OutgoingMail> ReadAllAsync(CancellationToken cancellationToken) =>
        channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class MailDispatcher(
    MailQueue queue,
    IGraphMailService mail,
    ILogger<MailDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                if (!mail.IsConfigured)
                {
                    logger.LogInformation("Graph mail is not configured; skipped {Subject} to {Recipients}",
                        item.Subject, string.Join(", ", item.To));
                    continue;
                }

                await mail.SendMailAsync(item.To, item.Subject, item.HtmlBody,
                    cancellationToken: stoppingToken);
                logger.LogInformation("Sent {Subject} to {Recipients}", item.Subject, string.Join(", ", item.To));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to send {Subject} to {Recipients}",
                    item.Subject, string.Join(", ", item.To));
            }
        }
    }
}
