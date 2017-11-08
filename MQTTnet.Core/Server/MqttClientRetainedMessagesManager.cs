﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet.Core.Packets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MQTTnet.Core.Server
{
    public sealed class MqttClientRetainedMessagesManager : IMqttClientRetainedMessageManager
    {
        private readonly Dictionary<string, MqttApplicationMessage> _retainedMessages = new Dictionary<string, MqttApplicationMessage>();
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly ILogger<MqttClientRetainedMessagesManager> _logger;
        private readonly MqttServerOptions _options;

        public MqttClientRetainedMessagesManager(IOptions<MqttServerOptions> options, ILogger<MqttClientRetainedMessagesManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task LoadMessagesAsync()
        {
            if (_options.Storage == null)
            {
                return;
            }

            await _gate.WaitAsync();
            try
            {
                var retainedMessages = await _options.Storage.LoadRetainedMessagesAsync();

                _retainedMessages.Clear();
                foreach (var retainedMessage in retainedMessages)
                {
                    _retainedMessages[retainedMessage.Topic] = retainedMessage;
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(new EventId(), exception, "Unhandled exception while loading retained messages.");
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task HandleMessageAsync(string clientId, MqttApplicationMessage applicationMessage)
        {
            if (applicationMessage == null) throw new ArgumentNullException(nameof(applicationMessage));

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var saveIsRequired = false;

                if (applicationMessage.Payload?.Any() == false)
                {
                    saveIsRequired = _retainedMessages.Remove(applicationMessage.Topic);
                    _logger.LogInformation("Client '{0}' cleared retained message for topic '{1}'.", clientId, applicationMessage.Topic);
                }
                else
                {
                    if (!_retainedMessages.ContainsKey(applicationMessage.Topic))
                    {
                        _retainedMessages[applicationMessage.Topic] = applicationMessage;
                        saveIsRequired = true;
                    }
                    else
                    {
                        var existingMessage = _retainedMessages[applicationMessage.Topic];
                        if (existingMessage.QualityOfServiceLevel != applicationMessage.QualityOfServiceLevel || !existingMessage.Payload.SequenceEqual(applicationMessage.Payload ?? new byte[0]))
                        {
                            _retainedMessages[applicationMessage.Topic] = applicationMessage;
                            saveIsRequired = true;
                        }
                    }
                    
                    _logger.LogInformation("Client '{0}' set retained message for topic '{1}'.", clientId, applicationMessage.Topic);
                }

                if (!saveIsRequired)
                {
                    _logger.LogTrace("Skipped saving retained messages because no changes were detected.");
                }

                if (saveIsRequired && _options.Storage != null)
                {
                    await _options.Storage.SaveRetainedMessagesAsync(_retainedMessages.Values.ToList());
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(new EventId(), exception, "Unhandled exception while handling retained messages.");
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<List<MqttApplicationMessage>> GetSubscribedMessagesAsync(MqttSubscribePacket subscribePacket)
        {
            var retainedMessages = new List<MqttApplicationMessage>();

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                foreach (var retainedMessage in _retainedMessages.Values)
                {
                    foreach (var topicFilter in subscribePacket.TopicFilters)
                    {
                        if (retainedMessage.QualityOfServiceLevel < topicFilter.QualityOfServiceLevel)
                        {
                            continue;
                        }

                        if (!MqttTopicFilterComparer.IsMatch(retainedMessage.Topic, topicFilter.Topic))
                        {
                            continue;
                        }

                        retainedMessages.Add(retainedMessage);
                        break;
                    }
                }
            }
            finally
            {
                _gate.Release();
            }

            return retainedMessages;
        }
    }
}
