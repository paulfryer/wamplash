﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using CacheSharp;
using CacheSharp.Redis;
using Newtonsoft.Json;
using Wamplash.Handlers;
using Wamplash.Messages;

namespace Wamplash.Redis.Handlers
{
    public abstract class RedisWampWebSocketHandler : WampWebSocketHandler
    {
        private readonly RedisCache cache = new RedisCache();

        // TODO: think about using redis pub/sub to sync this dictionary with central cache. That could allow us to support hoizontal scale and have a 
        // stateless architecture, so if a node goes down another node can continue to support the clients subscriptions that used the down node.
        // We probably just need to sync add/remove of subscriptions in the dictionary. 
        

        /// <summary>
        /// Key = Topic, Value = List of subscription ids.
        /// </summary>
        //private readonly Dictionary<string, List<long>> subscriptions = new Dictionary<string, List<long>>();

        private readonly Dictionary<string, long> subscriptions = new Dictionary<string, long>(); 

        protected RedisWampWebSocketHandler() : this(
            ConfigurationManager.AppSettings.Get("Redis.Endpoint"),
            ConfigurationManager.AppSettings.Get("Redis.Key"),
            ConfigurationManager.AppSettings.Get("Redis.UseSsl")
            )
        {
        }

        protected RedisWampWebSocketHandler(string endpoint, string key, string useSsl)
        {
            cache.Initialize(
                new Dictionary<string, string>
                {
                    {"Endpoint", endpoint},
                    {"Key", key},
                    {"UseSsl", useSsl}
                });
            cache.MessageReceived += OnMessageReceived;
            Subscribe += OnSubscribe;
            Unsubscribe += OnUnsubscribe;
            Publish += OnPublish;
            Event += OnEvent;
        }

        private void OnUnsubscribe(object sender, UnsubscribeMessage message)
        {
            if (subscriptions.ContainsValue(message.SubscriptionId))
            {
                var topic = subscriptions.Single(s => s.Value == message.SubscriptionId).Key;
                cache.UnsubscribeAsync(topic);
                subscriptions.Remove(topic);
            }

            Send(new UnsubscribedMessage
            {
                RequestId = message.RequestId
            });
        }

        private void OnEvent(object sender, EventMessage message)
        {
            if (subscriptions.ContainsValue(message.SubscriptionId))
                Send(message);
        }

        private void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            var publishMessage = JsonConvert.DeserializeObject<PublishMessage>(e.Value);

            

            foreach (var subscription in subscriptions.Where(s => s.Key == publishMessage.Topic))
            {
                var @event = new EventMessage(subscription.Value, publishMessage.RequestId, null, publishMessage.Details);
                    RaiseEvent(@event);
            }
        }

        private void OnPublish(object sender, PublishMessage message)
        {
            var value = JsonConvert.SerializeObject(message);
            cache.PublishAsync(message.Topic, value);
        }

        private void OnSubscribe(object sender, SubscribeMessage message)
        {
            var subscriptionId = UniqueIdGenerationService.GenerateUniqueId();
            
            if (!subscriptions.ContainsKey(message.Topic))
            {
                cache.SubscribeAsync(message.Topic);
                subscriptions.Add(message.Topic, subscriptionId);
            }
            else
            {
                subscriptions[message.Topic] = subscriptionId;
            }

            
           

            Send(new SubscribedMessage(message.RequestId, subscriptionId));
        }

    }
}