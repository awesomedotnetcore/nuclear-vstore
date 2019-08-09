using System;

using Confluent.Kafka;

using Microsoft.Extensions.Logging;

using NuClear.VStore.Options;

namespace NuClear.VStore.Kafka
{
    public abstract class ConsumerWrapper : IDisposable
    {
        protected ConsumerWrapper(ILogger logger, KafkaOptions kafkaOptions, bool enablePartitionEof, string groupId = null)
        {
            Logger = logger;
            var config = CreateConsumerConfig(kafkaOptions, enablePartitionEof, groupId);
            Consumer = new ConsumerBuilder<string, string>(config)
                       .SetKeyDeserializer(Deserializers.Utf8)
                       .SetValueDeserializer(Deserializers.Utf8)
                       .SetLogHandler(OnLog)
                       .SetErrorHandler(OnError)
                       .SetStatisticsHandler(OnStatistics)
                       .Build();
        }

        protected IConsumer<string, string> Consumer { get; }

        protected ILogger Logger { get; }

        public virtual void Dispose() => Consumer?.Dispose();

        private static ConsumerConfig CreateConsumerConfig(KafkaOptions kafkaOptions, bool enablePartitionEof, string groupId)
            => new ConsumerConfig
                {
                    BootstrapServers = kafkaOptions.BrokerEndpoints,
                    ApiVersionRequest = true,
                    GroupId = !string.IsNullOrEmpty(groupId) ? groupId : Guid.NewGuid().ToString(),
                    EnableAutoCommit = kafkaOptions.Consumer.EnableAutoCommit,
                    FetchWaitMaxMs = kafkaOptions.Consumer.FetchWaitMaxMs,
                    FetchErrorBackoffMs = kafkaOptions.Consumer.FetchErrorBackoffMs,
                    FetchMaxBytes = kafkaOptions.Consumer.FetchMessageMaxBytes,
                    MessageMaxBytes = kafkaOptions.Consumer.MessageMaxBytes,
                    QueuedMinMessages = kafkaOptions.Consumer.QueuedMinMessages,
                    AutoOffsetReset = AutoOffsetReset.Earliest,
                    EnablePartitionEof = enablePartitionEof,
#if DEBUG
                    Debug = "consumer,cgrp,topic,fetch"
#else
                    LogConnectionClose = false
#endif
                };

        private void OnLog(IConsumer<string, string> sender, LogMessage logMessage)
            => Logger.LogDebug(
                "Consuming from Kafka. Client: '{kafkaClient}', syslog level: '{kafkaLogLevel}', message: '{kafkaLogMessage}'.",
                logMessage.Name,
                logMessage.Level,
                logMessage.Message);

        private void OnError(IConsumer<string, string> sender, Error error)
            => Logger.LogInformation("Consuming from Kafka. Client error: '{kafkaError}'. No action required.", error);

        private void OnStatistics(IConsumer<string, string> sender, string json)
            => Logger.LogDebug("Consuming from Kafka. Statistics: '{kafkaStatistics}'.", json);
    }
}