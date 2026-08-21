using System;
using System.Collections.Generic;
using System.Text;

namespace Windose.System.Kernel.Subsystem
{
    public static class Session
    {
        public static UserAccount CurrentUser { get; set; }
        public static bool isElevated
        {
            get
            {
                if (CurrentUser == null)
                {
                    return false;
                }

                return CurrentUser.privilege is PrivilegeLevel.Administrator or PrivilegeLevel.System;
            }
            set
            {
                isElevated = value;
            }
        }

        public static bool TryElevate(string password)
        {
            if (CurrentUser == null)
            {
                return false;
            }
            if (CurrentUser.VerifyPassword(password))
            {
                isElevated = true;
                return true;
            }
            return false;
        }

        public static void EndElevate()
        {
            isElevated = false;
        }

        public static void Logout()
        {
            CurrentUser = null;
            isElevated = false;
        }

        public static void LogIn(string username, string password)
        {
            //todo: Implement login functionality
        }



    }
}
