using System;
using System.Security.Cryptography;

class Program
{
    static void Main(string[] args)
    {
        string? username = null;
        string? password = null;

        if (args.Length >= 2)
        {
            username = args[0];
            password = args[1];
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            Console.Write("Username: ");
            username = Console.ReadLine();
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            Console.Write("New password: ");
            password = ReadPassword();
            Console.WriteLine();
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            Console.WriteLine("Username and password are required.");
            return;
        }

        var hash = HashPassword(password);

        Console.WriteLine("\n-- Generated PBKDF2 password hash (Base64) --");
        Console.WriteLine(hash);

        var sql = $"UPDATE Users SET PasswordHash = '{hash.Replace("'","''")}' WHERE LOWER(Username) = '{username.Trim().ToLower().Replace("'","''") }';";

        Console.WriteLine("\n-- SQL to run on your database --");
        Console.WriteLine(sql);
    }

    private static string HashPassword(string password)
    {
        const int iterations = 10000;
        const int saltLength = 16;

        byte[] salt = new byte[saltLength];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }

        using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations))
        {
            byte[] hash = pbkdf2.GetBytes(32);
            byte[] hashWithSalt = new byte[salt.Length + hash.Length];
            Buffer.BlockCopy(salt, 0, hashWithSalt, 0, salt.Length);
            Buffer.BlockCopy(hash, 0, hashWithSalt, salt.Length, hash.Length);
            return Convert.ToBase64String(hashWithSalt);
        }
    }

    private static string ReadPassword()
    {
        var pwd = string.Empty;
        ConsoleKeyInfo key;
        do
        {
            key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Backspace && pwd.Length > 0)
            {
                pwd = pwd.Substring(0, pwd.Length - 1);
                Console.Write("\b \b");
            }
            else if (!char.IsControl(key.KeyChar))
            {
                pwd += key.KeyChar;
                Console.Write("*");
            }
        } while (key.Key != ConsoleKey.Enter);
        return pwd;
    }
}
