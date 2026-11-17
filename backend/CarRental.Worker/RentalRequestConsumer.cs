using Azure.Messaging.ServiceBus;
using CarRental.Api.Messaging;
using CarRental.Api.Services;
using Microsoft.Extensions.Options;

namespace CarRental.Worker;

/// <summary>
/// Odbiera wiadomości z kolejki rental-requests (Azure Service Bus) i przekazuje je do RentalConfirmationProcessor.
/// Udało się: Complete. Błąd (np. baza chwilowo niedostępna): Abandon, wiadomość wraca do kolejki,
/// a po 10 próbach (MaxDeliveryCount kolejki) trafia do dead-letter. Zła treść od razu do dead-letter.
/// </summary>
public sealed class RentalRequestConsumer(
    ServiceBusClient client,
    IServiceScopeFactory scopeFactory,
    IOptions<ServiceBusOptions> serviceBusOptions,
    IOptions<WorkerOptions> workerOptions,
    ILogger<RentalRequestConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queue = serviceBusOptions.Value.QueueName;
        await using var processor = client.CreateProcessor(queue, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = workerOptions.Value.MaxConcurrentCalls,
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5)
        });

        processor.ProcessMessageAsync += HandleMessageAsync;
        processor.ProcessErrorAsync += HandleErrorAsync;

        await processor.StartProcessingAsync(stoppingToken);
        logger.LogInformation("Nasłuchuję kolejki {Queue}.", queue);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normalne zatrzymanie
        }

        await processor.StopProcessingAsync(CancellationToken.None);
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var message = RentalRequestMessage.TryParse(args.Message.Body.ToString());
        if (message is null)
        {
            logger.LogWarning("Wiadomość {MessageId} ma złą treść, idzie do dead-letter.", args.Message.MessageId);
            await args.DeadLetterMessageAsync(args.Message, "InvalidBody", "Oczekiwano JSON {\"rentalId\":\"<guid>\"}.", args.CancellationToken);
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var confirmation = scope.ServiceProvider.GetRequiredService<RentalConfirmationProcessor>();
            await confirmation.ProcessAsync(message.RentalId, args.CancellationToken);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Nie udało się przetworzyć wniosku {RentalId} (próba {Attempt}).", message.RentalId, args.Message.DeliveryCount);
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
        }
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(args.Exception, "Błąd Service Bus ({Source}, {EntityPath}).", args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }
}
