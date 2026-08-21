using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Windose.System.Kernel.Subsystem
{
    public class UserAccount
    {
        public static List<UserAccount> accounts = new List<UserAccount>();
        public static string accountsFilePath = "/mnt/System/Accounts.dat";

        public string username;

        public byte[] password;
        public byte[] salt;

        public string profilePath;
        public PrivilegeLevel privilege;


        public UserAccount(string username, byte[] password, byte[] salt, string profilePath, PrivilegeLevel role)
        {
            this.username = username;
            this.password = password;
            this.salt = salt;
            this.profilePath = profilePath;
            this.privilege = role;
        }

        public bool VerifyPassword(string password)
        {
            return true; //TODO: Implement password verification using SHA256 hashing
        }

        public void ChangePassword(string newPassword)
        {
            //TODO: Implement password change using SHA256 hashing
        }

        public static UserAccount CreateAccount(string username, string password, PrivilegeLevel role)
        {
            byte[] salt = GenerateSalt(16);
            //byte[] hash = HashPassword(password, salt);
            string profilePath = $"/mnt/Users/{username}";


            UserAccount account = new UserAccount(username, new byte[16], salt, profilePath, role);


            if (Directory.Exists(profilePath))
            {
                // Handle the case where the profile directory already exists (e.g., throw an exception or log a warning)
            }

            Directory.CreateDirectory(profilePath);

            accounts.Add(account);

            StringBuilder stringBuilder = new StringBuilder();

            foreach(UserAccount user in accounts)
            {
                stringBuilder.AppendLine($"{user.username}:{user.password}:{user.salt}:{user.privilege}");
            }

            File.WriteAllText(accountsFilePath, stringBuilder.ToString());

            return account;
        }

        private static byte[] GenerateSalt(int length)
        {
            byte[] salt = new byte[length];
            Random rng = new Random();

            rng.NextBytes(salt);

            return salt;
        }

        //TODO: Implement password hashing, this throws rn
        /// <summary>
        /// Hashes the password with the provided salt using SHA256.
        /// Provide an unhashed password and the salt to get the hashed password.
        /// </summary>
        /// <param name="password"></param>
        /// <param name="salt"></param>
        /// <returns></returns>
        private static byte[] HashPassword(string password, byte[] salt)
        {
            byte[] combined = Encoding.UTF8.GetBytes(password).Concat(salt).ToArray();
            using (SHA256 sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(combined);
            }
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
