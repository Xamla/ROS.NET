﻿using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xamla.Robotics.Ros.Async;

namespace Uml.Robotics.Ros
{
    public class Subscriber : ISubscriber
    {
        /// <summary>
        ///     Creates a ROS Subscriber
        /// </summary>
        /// <param name="topic">Topic name to subscribe to</param>
        /// <param name="nodeHandle">nodehandle</param>
        /// <param name="cb">callback function to be fired when message is received</param>
        public Subscriber(string topic, NodeHandle nodeHandle, ISubscriptionCallbackHelper cb)
            : base(topic)
        {
            this.topic = topic;
            this.nodeHandle = new NodeHandle(nodeHandle);
            this.helper = cb;
        }

        /// <summary>
        ///     Deep Copy of a subscriber
        /// </summary>
        /// <param name="s">Subscriber to copy</param>
        public Subscriber(Subscriber s)
            : base(s.topic)
        {
            this.topic = s.topic;
            this.nodeHandle = new NodeHandle(s.nodeHandle);
            this.helper = s.helper;
        }

        /// <summary>
        ///     Creates a ROS subscriber
        /// </summary>
        public Subscriber()
            : base(null)
        {
        }

        /// <summary>
        ///     Returns the number of publishers on the subscribers topic
        /// </summary>
        public int NumPublishers =>
            IsValid ? subscription.NumPublishers : 0;

        /// <summary>
        ///     Shutdown a subscriber gracefully.
        /// </summary>
        public override async Task Shutdown() =>
            await Unsubscribe().ConfigureAwait(false);
    }

    public abstract class ISubscriber : IDisposable
    {
        protected ISubscriber(string topic)
        {
            if (topic !=null)
            {
                this.topic = topic;
                subscription = TopicManager.Instance.GetSubscription(topic);
            }
        }

        public ISubscriptionCallbackHelper helper;
        public NodeHandle nodeHandle;
        internal Subscription subscription;
        public string topic = "";
        public bool unsubscribed;

        public bool IsValid =>
            !unsubscribed;

        public virtual async Task Unsubscribe()
        {
            if (!unsubscribed)
            {
                unsubscribed = true;
                await TopicManager.Instance.Unsubscribe(topic, helper).ConfigureAwait(false);
            }
        }

        public abstract Task Shutdown();

        public void Dispose()
        {
            Shutdown().Wait();
        }
    }
}
