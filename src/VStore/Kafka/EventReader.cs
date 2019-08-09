using System;
using System.Collections.Generic;
using System.Linq;

using Confluent.Kafka;

using Microsoft.Extensions.Logging;

using NuClear.VStore.Events;
using NuClear.VStore.Options;

namespace NuClear.VStore.Kafka
{
    public sealed class EventReader : ConsumerWrapper
    {
        private const int DefaultPartition = 0;

        public EventReader(ILogger logger, KafkaOptions kafkaOptions)
            : base(logger, kafkaOptions, true)
        {
        }

        public IReadOnlyCollection<KafkaEvent<TSourceEvent>> Read<TSourceEvent>(
            string topic, DateTime dateToStart)
            where TSourceEvent : IEvent
        {
            var events = new List<KafkaEvent<TSourceEvent>>();

            try
            {
                var offsets = GetOffsets(topic, dateToStart);
                Consumer.Assign(offsets);
                while (true)
                {
                    var message = Consumer.Consume(TimeSpan.FromMilliseconds(100));
                    if (message.IsPartitionEOF)
                    {
                        break;
                    }

                    var @event = Event.Deserialize<TSourceEvent>(message.Value);
                    events.Add(new KafkaEvent<TSourceEvent>(@event, message.TopicPartitionOffset, message.Timestamp));
                }
            }
            catch (ConsumeException ex)
            {
                Logger.LogError(ex, "Consuming error from topic '{topic}'. Error: {error}", topic, ex.Error);
            }

            return events;
        }

        private IEnumerable<TopicPartitionOffset> GetOffsets(string topic, DateTime date)
        {
            try
            {
                var timestampToSearch = new TopicPartitionTimestamp(topic, DefaultPartition, new Timestamp(date, TimestampType.CreateTime));
                return Consumer.OffsetsForTimes(new[] { timestampToSearch }, TimeSpan.FromSeconds(10))
                               .Select(x => x);
            }
            catch (KafkaException ex)
            {
                throw new EventReceivingException(
                    $"Unexpected error occured while getting offset for date '{date:o}' " +
                    $"in topic/partition '{topic}/{DefaultPartition}'",
                    ex);
            }
        }
    }
}