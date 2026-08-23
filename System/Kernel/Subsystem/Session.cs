using Windose.System.Kernel.Cryptography;

namespace Windose.System.Kernel.Subsystem
{
    public static class Session
    {
        public static UserAccount? CurrentUser { get; set; }
        public const string accountsFilePath = "/mnt/System/Accounts.dat";

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

        static Session()
        {
            LoadUserAccounts();
        }

        private static void LoadUserAccounts()
        {
            if (!File.Exists(accountsFilePath))
            {
                return;
            }

            string[] lines = File.ReadAllLines(accountsFilePath);

            foreach (string line in lines)
            {
                string[] args = line.Split(":");

                if (args.Length != 3)
                {
                    continue;
                }

                string username = args[0];
                string password = args[1];
                PrivilegeLevel privilegeLevel = ParsePrivilege(args[2]);

                UserAccount.accounts.Add(new UserAccount(username, password, privilegeLevel));
                
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

        public static bool LogIn(string username, string password)
        {
            if (!File.Exists(accountsFilePath))
            {
                return false;
            }
            string[] lines = File.ReadAllLines(accountsFilePath);

            foreach (string line in lines)
            {
                string[] args = line.Split(":");

                if (args.Length != 3)
                {
                    continue;
                }

                string argUsername = args[0];
                string argPassword = args[1];
                PrivilegeLevel privilegeLevel = ParsePrivilege(args[2]);

                //Username check
                if (argUsername != username)
                {
                    continue;
                }

                //Wrong password, TODO: Message windows
                if (SHA256.ComputeString(password) != argPassword)
                {
                    //Display a message
                    return false;
                }

                CurrentUser = new UserAccount(username, password, privilegeLevel);
                return true;
            }
            return false;

        }

        public static PrivilegeLevel ParsePrivilege(string priv)
        {
            switch (priv)
            {
                case "Guest": return PrivilegeLevel.Guest;
                case "Standart": return PrivilegeLevel.Standard;
                case "Administrator": return PrivilegeLevel.Administrator;
                case "System": return PrivilegeLevel.System;

                default: return PrivilegeLevel.Guest;
            }
        }
    }
}
