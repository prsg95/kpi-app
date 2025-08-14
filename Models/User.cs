using KpiMgmtApi.Models.Interfaces;
using System;
using System.Collections.Generic;

namespace KpiMgmtApi.Models
{
    public class UserResponse : IMetricResponse
    {
        public List<Users> Result { get; set; }
    }

    public class Users
    {
        public string Username { get; set; }
        public int User_Id { get; set; }
        public long Created { get; set; }
        public string Common { get; set; }
        public string Oracle_Maintained { get; set; }
        public string Inherited { get; set; }
        public string Default_Collation { get; set; }
        public string Implicit { get; set; }
        public string All_Shard { get; set; }
    }
}