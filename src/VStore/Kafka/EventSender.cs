using System;
using System.Threading.Tasks;

using Confluent.Kafka;

using Microsoft.Extensions.Logging;

using NuClear.VStore.Events;
using NuClear.VStore.Options;

namespace NuClear.VStore.Kafka
{
    public sealed class EventSender : IEventSender, IDisposable
    {
        private const int DefaultPartition = 0;

        private readonly ILogger<EventSender> _logger;
        private readonly IProducer<string, string> _producer;

        public EventSender(KafkaOptions kafkaOptions, ILogger<EventSender> logger)
        {
            _logger = logger;
            var config = new ProducerConfig
                {
                    BootstrapServers = kafkaOptions.BrokerEndpoints,
                    ApiVersionRequest = true,
                    LingerMs = kafkaOptions.Producer.QueueBufferingMaxMs,
                    QueueBufferingMaxKbytes = kafkaOptions.Producer.QueueBufferingMaxKbytes,
                    BatchNumMessages = kafkaOptions.Producer.BatchNumMessages,
                    MessageMaxBytes = kafkaOptions.Producer.MessageMaxBytes,
                    Acks = Acks.All,
                    MessageTimeoutMs = 5000,
#if DEBUG
                    Debug = "broker,topic,msg"
#else
                    LogConnectionClose = false
#endif
                };

            _producer = new ProducerBuilder<string, string>(config)
                        .SetKeySerializer(Serializers.Utf8)
                        .SetValueSerializer(Serializers.Utf8)
                        .SetLogHandler(OnLog)
                        .SetErrorHandler(OnLogError)
                        .SetStatisticsHandler(OnStatistics)
                        .Build();
        }

        public async Task SendAsync(string topic, IEvent @event)
        {
            var messageContent = @event.Serialize();
            try
            {
                var topicPartition = new TopicPartition(topic, new Partition(DefaultPartition));
                var message = new Message<string, string> { Key = @event.Key, Value = messageContent };
                var result = await _producer.ProduceAsync(topicPartition, message);
                _logger.LogDebug(
                    "Produced to Kafka. Topic/partition/offset: '{kafkaTopic}/{kafkaPartition}/{kafkaOffset}'. Message: '{kafkaMessage}'. Status: {status}.",
                    result.Topic,
                    result.Partition,
                    result.Offset,
                    message,
                    result.Status);
            }
            catch (ProduceException<string, string> ex)
            {
                _logger.LogError(
                    ex,
                    "Error producing to Kafka. Topic: '{kafkaTopic}'. Message: '{kafkaMessage}'. Error: {error}.",
                    topic,
                    messageContent,
                    ex.Error);

                throw new KafkaException(ex.Error);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error producing to Kafka. Topic: '{kafkaTopic}'. Message: '{kafkaMessage}'.",
                    topic,
                    messageContent);
                throw;
            }
        }

        public void Dispose() => _producer?.Dispose();

        private void OnLog(IProducer<string, string> sender, LogMessage logMessage)
            => _logger.LogDebug(
                "Producing to Kafka. Client: '{kafkaClient}', syslog level: '{kafkaLogLevel}', message: '{kafkaLogMessage}'.",
                logMessage.Name,
                logMessage.Level,
                logMessage.Message);

        private void OnLogError(IProducer<string, string> sender, Error error)
            => _logger.LogInformation("Producing to Kafka. Client error: '{kafkaError}'. No action required.", error);

        private void OnStatistics(IProducer<string, string> sender, string json)
            => _logger.LogDebug("Producing to Kafka. Statistics: '{kafkaStatistics}'.", json);
    }
}