﻿using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Discord.Commands;
using Discord.Addons.Interactive;

using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;

using MimeKit;

using WalrusBot2.Data;
using System.Net.Mail;

namespace WalrusBot2.Modules
{
    [RequireOwner()]
    [Group("verify")]
    [Name("User Verification")]
    public class VerifyModule : XModule
    {
        private static GmailService _gmailService = null;
        private static Random _random = new Random();

        public VerifyModule()
        {
            if(_gmailService == null)
            {
                _gmailService = new GmailService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = Program.GoogleCredential,
                    ApplicationName = database["config", "googleAppName"]
                });
            }
        }

        /// <todo>
        ///     <item> Ensure that users can't use each other's email addresses for verification. </item>
        ///     <item> Check that the program doesn't crash if attempting to save an email that already exists (unique key). </item>
        /// </todo>
        [Command("email", RunMode = RunMode.Async)]
        [RequireContext(ContextType.DM, ErrorMessage = "You've got to use this in a DM to me! I'll delete the email for you so no one sees :wink:")]
        [Summary("Enter your email with **'svge!verify email [your email]'** to get your personal verification code sent to you. " +
            "Use your University of Southampton email address to get access to the 'student' role so that you can vote at meetings and AGMs.")]
        public async Task EmailAsync([Remainder]string email)
        {
            if(!IsValidEmail(email))
            {
                await ReplyAsync("That doesn't appear to be a valid email address! Please try again.");
                return;
            }
            WalrusUserInfo userInfo = await database.WalrusUserInfoes.FindAsync(Context.User.Id.ToString());
            if (userInfo == null)
            {
                userInfo = new WalrusUserInfo
                {
                    UserId = Context.User.Id.ToString(),
                    Verified = false,
                    Username = Context.User.Username,
                    Email = email,
                    Code = RandomString(8)
                };
                if(await SendEmailAsync(email, userInfo.Code))
                {
                    database.WalrusUserInfoes.Add(userInfo);
                    await database.SaveChangesAsync();
                }
            }
            else
            {
                if (userInfo.Email == email)
                {
                    if (userInfo.Verified)
                        await ReplyAsync("You're already verified with that email! If you're missing student or membership roles then wait for an update, or ask a committee member to update it for you!");
                    else
                        await SendEmailAsync(email, userInfo.Code);
                }
                else
                {
                    userInfo.Email = email;
                    if(userInfo.Verified)
                    {
                        userInfo.Verified = false;
                        await ReplyAsync("Please note that you've already verified with a different email and you may lose access to your roles until you've verified this one!");
                        await ReplyAndDeleteAsync("Please type \"confirm\" if this is correct and you wish to change your email (30 second timeout)");
                        var response = await NextMessageAsync(new EnsureFromUserCriterion(Context.User.Id), timeout: TimeSpan.FromSeconds(31) );
                        if (!(response.Content.ToLower() == "confirm") )
                        {
                            await ReplyAsync("You didn't confirm your email change within the time limit. If you still wish to change your email then please rerun the command.");
                            return;
                        }

                    }
                    userInfo.Code = RandomString(8);
                    if(await SendEmailAsync(email, userInfo.Code))
                    {
                        await database.SaveChangesAsync();
                    }
                }
            }
        }

        [Command("messagenonverified")]
        [Summary("Send a message to all non-verified persons in the server asking them to do so.")]
        [RequireUserPermission(Discord.GuildPermission.ManageRoles)]
        public async Task MessageNonVerifiedAsync()
        {
            await ReplyAsync("Command not yet written...");
        }

        [Command("code")]
        [Summary("Enter the code sent to your email to verify your email address!s")]
        [RequireContext(ContextType.DM, ErrorMessage = "You're *supposed* to send this to me in a DM... Please go do that.")]
        public async Task CodeAsync(string code)
        {
            await ReplyAsync($"Your code is **`{code}`**");
        }

        #region Utility Functions
        /// <summary>
        /// Sends an email to the given email address, substituting the given code into the email template.
        /// </summary>
        /// <param name="emailAddr"></param>
        /// <param name="code"></param>
        /// <returns></returns>
        private async Task<bool> SendEmailAsync(string emailAddr, string code)
        {
            if (!IsValidEmail(emailAddr)) return false;  // running it here again just to be safe, probably unncessary...
            MimeMessage message = new MimeMessage();
            message.To.Add(new MailboxAddress(Context.User.Username.ToString(), emailAddr));
            message.From.Add(new MailboxAddress(database["config", "gmailFromName"], database["config", "gmailFromAddr"]));
            message.Subject = "SVGE Discord Verification Email!";
            // HTML body of email
            var body = new BodyBuilder();
            string htmlString = await File.OpenText(database["config", "emailTemplatePath"]).ReadToEndAsync();
            body.HtmlBody = htmlString.Replace("xXxCODEHERExXx", code);
            message.Body = body.ToMessageBody();

            var gMessage = new Message() { Raw = MimeToGmail(message.ToString()) };
            try
            {
                await _gmailService.Users.Messages.Send(gMessage, "me").ExecuteAsync();
                await ReplyAsync("Verification email sent! Once you've got your code, send it to me with *svge!verify code* ***[your-code-here]***.");
                return true;
            }
            catch (Exception e)
            {
                await ReplyAsync("There was an issue with sending your email! Try again in a few minutes, and if the problem persists then please contact a committee member.");
                Console.WriteLine($"Exception when sending an email: {e.ToString()}");
                return false;
            }
        }

        /// <summary>
        /// Returns a string of random characters with length "length".
        /// </summary>
        /// <param name="length"></param>
        /// <returns></returns>
        private string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcdefghijklmnopqrstuvwxyz";
            return new string(Enumerable.Repeat(chars, length)
              .Select(s => s[_random.Next(s.Length)]).ToArray());
        }

        private string MimeToGmail(string msg)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(msg);

            return System.Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .Replace("=", "");
        }

        /// <summary>
        /// Confirms whether the supplied string is a valid email address.
        /// </summary>
        /// <param name="emailAddr"></param>
        /// <returns></returns>
        private bool IsValidEmail(string emailAddr)
        {
            try
            {
                MailAddress m = new MailAddress(emailAddr);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
        #endregion
    }
}
