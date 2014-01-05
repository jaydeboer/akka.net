﻿using Pigeon.Messaging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Pigeon.Actor
{
    public partial class ActorCell 
    {
        private void OnReceiveInternal(object message)
        {
            try
            {
                if (message is SystemMessage)
                {
                    Pattern.Match(message)
                        //add watcher
                        .With<Kill>(Kill)
                        .With<Stop>(m => Stop())
                        .With<StopChild>(m => StopChild(m.Child))
                        .With<Restart>(m => Restart())
                        .With<RestartChild>(m => RestartChild(m.Child))
                        .With<PoisonPill>(PoisonPill)
						//someone is watching us
                        .With<Watch>(Watch)
						//someone is unwatching us
                        .With<Unwatch>(Unwatch)
                        //complete the future callback by setting the result in this thread
                        .With<CompleteFuture>(CompleteFuture)
                        //resolve time distance to actor
                        .With<Ping>(Ping)
                        //supervice exception from child actor
                        .With<SuperviceChild>(SuperviceChild)
                        //handle any other message
                        .With<Identity>(Identity)
                        .Default(m => { throw new NotImplementedException(); });
                }
                else
                {
                    Default(message);
                }
            }
            catch (Exception reason)
            {
				//TODO: do something nicer here
                if (Parent is ActorCell)
                {					
                    ((ActorCell)Parent).Self.Tell(new SuperviceChild(reason));
                }
            }
        }		

		private void Identity(Identity m)
        {
            Sender.Tell(new ActorIdentity(m.MessageId, this.Self));
        }

        private void PoisonPill(PoisonPill m)
        {

        }

		/// <summary>
		/// Async restart this actor
		/// </summary>
        public void Restart()
        {
            ((ActorCell)this.Parent).Self.Tell(new RestartChild(this.Self));
        }

        private void RestartChild(LocalActorRef child)
        {
            StopChild(child);
            Debug.WriteLine("restarting child: {0}", child.Path);
            Unbecome();//unbecome deadletters
            NewActor(child.Cell);
        }

		/// <summary>
		/// Async stop this actor
		/// </summary>
        public void Stop()
        {
            ((ActorCell)this.Parent).Self.Tell(new StopChild(this.Self));
        }

        private void StopChild(LocalActorRef child)
        {
            Debug.WriteLine("stopping child: {0}", child.Path);
            child.Cell.Become(System.DeadLetters.Tell);
            LocalActorRef tmp;
            var name = child.Path.Name;
            this.Children.TryRemove(name, out tmp);
        }

        private void Kill(Kill m)
        {
            throw new ActorKilledException("Kill");
        }
        private void Default(object m)
        {
            CurrentBehavior(m);
        }

        private void SuperviceChild(SuperviceChild m)
        {
            switch (this.Actor.SupervisorStrategyLazy().Handle(Sender, m.Reason))
            {
                case Directive.Escalate:
                    throw m.Reason;
                case Directive.Resume:
                    break;
                case Directive.Restart:
                    RestartChild((LocalActorRef)Sender);
                    break;
                case Directive.Stop:
                    StopChild((LocalActorRef)Sender);
                    break;
                default:
                    break;
            }
        }

        private void Ping(Ping m)
        {
            Sender.Tell(
                        new Pong
                        {
                            LocalUtcNow = m.LocalUtcNow,
                            RemoteUtcNow = DateTime.UtcNow
                        });
        }

        protected BroadcastActorRef Watchers = new BroadcastActorRef();
        private void Watch(Watch m)
        {
            Watchers.Add(Sender);
        }

        private void Unwatch(Unwatch m)
        {
            Watchers.Remove(Sender);
        }

        private void CompleteFuture(CompleteFuture m)
        {
            m.SetResult();
        }
    }
}
