﻿#region Copyright 2014 Exceptionless

// This program is free software: you can redistribute it and/or modify it 
// under the terms of the GNU Affero General Public License as published 
// by the Free Software Foundation, either version 3 of the License, or 
// (at your option) any later version.
// 
//     http://www.gnu.org/licenses/agpl-3.0.html

#endregion

using System;
using System.Linq;
using System.Threading.Tasks;
using Exceptionless.Core.Extensions;
using Exceptionless.Core.Messaging;
using Exceptionless.Core.Messaging.Models;
using Exceptionless.Models;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;

namespace Exceptionless.Api.Hubs {
    [HubName("messages")]
    public class MessageBusHub : Hub {
        private readonly ConnectionMapping _userIdConnections = new ConnectionMapping();

        public MessageBusHub(IMessageSubscriber subscriber) {
            subscriber.Subscribe<EntityChanged>(OnEntityChanged);
            subscriber.Subscribe<PlanChanged>(OnPlanChanged);
            subscriber.Subscribe<PlanOverage>(OnPlanOverage);
            subscriber.Subscribe<UserMembershipChanged>(OnUserMembershipChanged);
        }

        private void OnUserMembershipChanged(UserMembershipChanged userMembershipChanged) {
            if (userMembershipChanged == null)
                return;

            if (String.IsNullOrEmpty(userMembershipChanged.OrganizationId))
                return;

            // manage user organization group membership
            foreach (var connectionId in _userIdConnections.GetConnections(userMembershipChanged.UserId)) {
                if (userMembershipChanged.ChangeType == ChangeType.Added)
                    Groups.Add(connectionId, userMembershipChanged.OrganizationId).Wait();
                else if (userMembershipChanged.ChangeType == ChangeType.Removed)
                    Groups.Remove(connectionId, userMembershipChanged.OrganizationId).Wait();
            }

            var group = Clients.Group(userMembershipChanged.OrganizationId);
            if (group != null)
                group.userMembershipChanged(userMembershipChanged);
        }

        private void OnEntityChanged(EntityChanged entityChanged) {
            if (entityChanged == null)
                return;

            if (entityChanged.Type == typeof(User).Name && Clients.User(entityChanged.Id) != null) {
                Clients.User(entityChanged.Id).entityChanged(entityChanged);
                return;
            }

            if (String.IsNullOrEmpty(entityChanged.OrganizationId))
                return;

            var group = Clients.Group(entityChanged.OrganizationId);
            if (group != null)
                group.entityChanged(entityChanged);
        }

        private void OnPlanOverage(PlanOverage planOverage) {
            if (planOverage == null)
                return;

            var group = Clients.Group(planOverage.OrganizationId);
            if (group != null)
                group.planOverage(planOverage);
        }

        private void OnPlanChanged(PlanChanged planChanged) {
            if (planChanged == null)
                return;

            var group = Clients.Group(planChanged.OrganizationId);
            if (group != null)
                group.planChanged(planChanged);
        }

        public override Task OnConnected() {
            foreach (string organizationId in Context.User.GetOrganizationIds())
                Groups.Add(Context.ConnectionId, organizationId);

            _userIdConnections.Add(Context.User.GetUserId(), Context.ConnectionId);

            return base.OnConnected();
        }

        public override Task OnDisconnected(bool stopCalled) {
            _userIdConnections.Remove(Context.User.GetUserId(), Context.ConnectionId);

            return base.OnDisconnected(stopCalled);
        }

        public override Task OnReconnected() {
            foreach (string organizationId in Context.User.GetOrganizationIds())
                Groups.Add(Context.ConnectionId, organizationId);

            if (!_userIdConnections.GetConnections(Context.User.GetUserId()).Contains(Context.ConnectionId))
                _userIdConnections.Add(Context.User.GetUserId(), Context.ConnectionId);

            return base.OnReconnected();
        }
    }
}