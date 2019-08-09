using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;

using Microsoft.Extensions.Logging;

using NuClear.VStore.Events;
using NuClear.VStore.Options;

namespace NuClear.VStore.Kafka
{
    public sealed class EventReceiver : ConsumerWrapper
    {
        private readonly IEnumerable<string> _topics;

        private bool _streamingStarted;

        private event EventHandler<ConsumeResult<string, string>> OnMessage;

        public EventReceiver(ILogger logger, KafkaOptions kafkaOptions, string groupId, IEnumerable<string> topics)
            : base(logger, kafkaOptions, false, groupId)
        {
            _topics = topics;
        }

        public IObservable<KafkaEvent<TSourceEvent>> Subscribe<TSourceEvent>(CancellationToken cancellationToken)
            where TSourceEvent : IEvent
        {
            if (_streamingStarted)
            {
                throw new InvalidOperationException("Streaming already started. Please dispose the previous observable before getting the new one.");
            }

            var pollCancellationTokenSource = new CancellationTokenSource();
            var registration = cancellationToken.Register(() => pollCancellationTokenSource.Cancel());

            var onMessage = Observable.FromEventPattern<ConsumeResult<string, string>>(
                                          x =>
                                              {
                                                  OnMessage += x;
                                                  Consumer.Subscribe(_topics);
                                              },
                                          x =>
                                              {
                                                  OnMessage -= x;
                                                  pollCancellationTokenSource.Cancel();
                                                  registration.Dispose();
                                                  Consumer.Unsubscribe();
                                                  _streamingStarted = false;
                                              })
                                      .Select(x => x.EventArgs)
                                      .Select(x =>
                                                  {
                                                      var @event = Event.Deserialize<TSourceEvent>(x.Value);
                                                      return new KafkaEvent<TSourceEvent>(@event, x.TopicPartitionOffset, x.Timestamp);
                                                  });
            Task.Factory.StartNew(
                    () =>
                        {
                            while (!pollCancellationTokenSource.IsCancellationRequested)
                            {
                                var cr = Consumer.Consume(pollCancellationTokenSource.Token);
                                OnMessage?.Invoke(Consumer, cr);
                            }
                        },
                    pollCancellationTokenSource.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default)
                .ConfigureAwait(false);

            _streamingStarted = true;

            return onMessage;
        }

        public void Commit<TSourceEvent>(KafkaEvent<TSourceEvent> @event) where TSourceEvent : IEvent
        {
            var offsetsToCommit = new TopicPartitionOffset(@event.TopicPartitionOffset.TopicPartition, @event.TopicPartitionOffset.Offset + 1);
            Consumer.Commit(new[] { offsetsToCommit });
        }
    }
}