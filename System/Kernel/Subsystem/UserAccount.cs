using Windose.System.Kernel.Cryptography;

namespace Windose.System.Kernel.Subsystem
{
    public class UserAccount
    {
        public static List<UserAccount> accounts = new List<UserAccount>();
        

        public string username;

        public string password;

        public string profilePath;
        public PrivilegeLevel privilege;


        public UserAccount(string username, string password, string profilePath, PrivilegeLevel role)
        {
            this.username = username;
            this.password = password;
            this.profilePath = profilePath;
            this.privilege = role;
        }

        public UserAccount(string username, string password, PrivilegeLevel role)
        {
            this.username = username;
            this.password = password;
            this.profilePath = $"/mnt/Users/{username}";
            this.privilege = role;
        }

        public bool VerifyPassword(string password)
        {
            if(SHA256.ComputeString(password) != password)
            {
                //Display message
                return false;
            }
            return true;
        }

        public void ChangePassword(string newPassword)
        {

            string[] lines = File.ReadAllLines(Session.accountsFilePath);

            

            for (int i = 0; i < lines.Length; i++)
            {
                string[] args = lines[i].Split(':');

                if (args.Length != 3)
                    continue;

                if (args[0] != username)
                    continue;

                lines[i] = $"{username}:{SHA256.ComputeString(newPassword)}:{privilege}";
                break;
            }

            File.WriteAllLines(Session.accountsFilePath, lines);
        }

        /// <summary>
        /// Returns a new User account if one doesn't exist already
        /// </summary>
        /// <param name="username"></param>
        /// <param name="password"></param>
        /// <param name="role"></param>
        /// <returns></returns>
        public static UserAccount ?CreateAccount(string username, string password, PrivilegeLevel role)
        {
            string hash = SHA256.ComputeString(password);
            string profilePath = $"/mnt/Users/{username}";


            UserAccount account = new UserAccount(username, hash, profilePath, role);


            if (Directory.Exists(profilePath))
            {
                return null;
                // Handle the case where the profile directory already exists (e.g., throw an exception or log a warning)
            }

            Directory.CreateDirectory(profilePath);

            accounts.Add(account);

            File.AppendAllText(Session.accountsFilePath, $"{account.username}:{account.password}:{account.privilege}");

            return account;
        }

    }

    public enum PrivilegeLevel
    {
        System,
        Administrator,
        Standard,
        Guest
    }
}
