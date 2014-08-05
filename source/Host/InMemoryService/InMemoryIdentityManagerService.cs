﻿/*
 * Copyright (c) Dominick Baier, Brock Allen.  All rights reserved.
 * see license
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Thinktecture.IdentityManager.Core;

namespace Thinktecture.IdentityManager.Host
{
    class InMemoryIdentityManagerService : IIdentityManagerService
    {
        ICollection<InMemoryUser> users;
        public InMemoryIdentityManagerService(ICollection<InMemoryUser> users)
        {
            this.users = users;
        }

        public System.Threading.Tasks.Task<IdentityManagerMetadata> GetMetadataAsync()
        {
            return Task.FromResult(new IdentityManagerMetadata()
            {
                UserMetadata = new UserMetadata
                {
                    SupportsCreate = true,
                    SupportsDelete = true,
                    SupportsClaims = true,
                    Properties = new HashSet<PropertyMetadata>
                    {
                        new PropertyMetadata {
                            Name = "Username",
                            Type = Constants.ClaimTypes.Username,
                            Required = true,
                        },
                        new PropertyMetadata {
                            Name = "Full Name",
                            Type = Constants.ClaimTypes.Name,
                            Required = true,
                        },
                        new PropertyMetadata {
                            Name = "Password",
                            Type = Constants.ClaimTypes.Password,
                            DataType = PropertyDataType.Password,
                            Required = true,
                        },
                        new PropertyMetadata {
                            Name = "Email",
                            Type = Constants.ClaimTypes.Email,
                            DataType = PropertyDataType.Email,
                        },
                        new PropertyMetadata {
                            Name = "Phone",
                            Type = Constants.ClaimTypes.Phone,
                        },
                        new PropertyMetadata {
                            Name = "Is Administrator",
                            Type = "role.admin",
                            DataType = PropertyDataType.Boolean
                        },
                        new PropertyMetadata {
                            Name = "First Name",
                            Type = "first",
                        },
                        new PropertyMetadata {
                            Name = "Last Name",
                            Type = "last",
                        },
                    }
                }
            });
        }

        public System.Threading.Tasks.Task<IdentityManagerResult<CreateResult>> CreateUserAsync(string username, string password)
        {
            string[] errors = ValidateProperty(Constants.ClaimTypes.Password, password);
            if (errors != null)
            {
                return Task.FromResult(new IdentityManagerResult<CreateResult>(errors));
            }

            var user = new InMemoryUser()
            {
                Username = username,
                Password = password
            };
            users.Add(user);

            return Task.FromResult(new IdentityManagerResult<CreateResult>(new CreateResult() { Subject = user.Subject }));
        }

        public System.Threading.Tasks.Task<IdentityManagerResult> DeleteUserAsync(string subject)
        {
            var user = users.SingleOrDefault(x => x.Subject == subject);
            if (user != null)
            {
                users.Remove(user);
            }
            return Task.FromResult(IdentityManagerResult.Success);
        }

        public System.Threading.Tasks.Task<IdentityManagerResult<QueryResult>> QueryUsersAsync(string filter, int start, int count)
        {
            var query =
                from u in users
                select u;
            if (!String.IsNullOrWhiteSpace(filter))
            {
                filter = filter.ToLower();
                query =
                    from u in query
                    let name = (from c in u.Claims where c.Type == "name" select c.Value).SingleOrDefault()
                    where
                        u.Username.ToLower().Contains(filter) ||
                        (name != null && name.ToLower().Contains(filter))
                    select u;
            }

            var userResults =
                from u in query.Distinct()
                select new UserResult
                {
                    Subject = u.Subject,
                    Username = u.Username,
                    Name = u.Claims.Where(x => x.Type == Constants.ClaimTypes.Name).Select(x => x.Value).FirstOrDefault(),
                };

            var result = userResults.Skip(start).Take(count);
            return Task.FromResult(new IdentityManagerResult<QueryResult>(new QueryResult
            {
                Filter = filter,
                Start = start,
                Count = result.Count(),
                Users = result,
                Total = userResults.Count(),
            }));
        }

        public System.Threading.Tasks.Task<IdentityManagerResult<UserDetail>> GetUserAsync(string subject)
        {
            var user = users.SingleOrDefault(x => x.Subject == subject);
            if (user == null)
            {
                return Task.FromResult(new IdentityManagerResult<UserDetail>((UserDetail)null));
            }

            var props = new List<UserClaim>()
            {
                new UserClaim{Type=Constants.ClaimTypes.Username, Value=user.Username},
                new UserClaim{Type=Constants.ClaimTypes.Name, Value=user.Claims.GetValue(Constants.ClaimTypes.Name)},
                new UserClaim{Type=Constants.ClaimTypes.Password, Value=""},
                new UserClaim{Type=Constants.ClaimTypes.Email, Value=user.Email},
                new UserClaim{Type=Constants.ClaimTypes.Phone, Value=user.Mobile},
                new UserClaim{Type="role.admin", Value=user.Claims.HasValue(Constants.ClaimTypes.Role, "Admin").ToString().ToLower()},
                new UserClaim{Type="first", Value=user.FirstName},
                new UserClaim{Type="last", Value=user.LastName},
            };
            var claims = user.Claims.Where(x=>!(x.Type == Constants.ClaimTypes.Role && x.Value == "Admin")).Select(x => new UserClaim { Type = x.Type, Value = x.Value });

            return Task.FromResult(new IdentityManagerResult<UserDetail>(new UserDetail
            {
                Subject = user.Subject,
                Username = user.Username,
                Name = user.Claims.GetValue(Constants.ClaimTypes.Name),
                Properties = props,
                Claims = claims
            }));
        }

        public System.Threading.Tasks.Task<IdentityManagerResult> SetPropertyAsync(string subject, string type, string value)
        {
            var user = users.SingleOrDefault(x => x.Subject == subject);
            if (user == null)
            {
                return Task.FromResult(new IdentityManagerResult("No user found"));
            }

            var errors = ValidateProperty(type, value);
            if (errors != null)
            {
                return Task.FromResult(new IdentityManagerResult(errors));
            }

            switch(type)
            {
                case Constants.ClaimTypes.Name:
                    {
                        user.Claims.SetValue(Constants.ClaimTypes.Name, value);
                    }
                    break;
                case Constants.ClaimTypes.Password:
                    user.Password = value;
                    break;
                case Constants.ClaimTypes.Email:
                    user.Email = value;
                    break;
                case Constants.ClaimTypes.Phone:
                    user.Mobile = value;
                    break;
                case "role.admin":
                    {
                        var val = Boolean.Parse(value);
                        if (val)
                        {
                            user.Claims.AddClaim(Constants.ClaimTypes.Role, "admin");
                        }
                        else
                        {
                            user.Claims.RemoveClaim(Constants.ClaimTypes.Role, "admin");
                        }
                    }
                    break;
                case "first":
                    user.FirstName = value;
                    break;
                case "last":
                    user.LastName = value;
                    break;
                default:
                    throw new InvalidOperationException("Invalid Property Type");
            }

            return Task.FromResult(IdentityManagerResult.Success);
        }

        private string[] ValidateProperty(string type, string value)
        {
            switch (type)
            {
                case ClaimTypes.CookiePath:
                    {
                        if (String.IsNullOrWhiteSpace(value))
                        {
                            return new string[] { "Password required" };
                        }
                        if (value.Length < 3)
                        {
                            return new string[] { "Password must have at least 3 characters" };
                        }
                    }
                    break;
            } 
            return null;
        }

        public System.Threading.Tasks.Task<IdentityManagerResult> AddClaimAsync(string subject, string type, string value)
        {
            var user = users.SingleOrDefault(x => x.Subject == subject);
            if (user == null)
            {
                return Task.FromResult(new IdentityManagerResult("No user found"));
            }

            user.Claims.AddClaim(type, value);
            
            return Task.FromResult(IdentityManagerResult.Success);
        }

        public System.Threading.Tasks.Task<IdentityManagerResult> RemoveClaimAsync(string subject, string type, string value)
        {
            var user = users.SingleOrDefault(x => x.Subject == subject);
            if (user == null)
            {
                return Task.FromResult(new IdentityManagerResult("No user found"));
            }

            user.Claims.RemoveClaims(type, value);

            return Task.FromResult(IdentityManagerResult.Success);
        }
    }
}