﻿using Aurora.Framework;
using Aurora.Framework.ClientInterfaces;
using Aurora.Framework.DatabaseInterfaces;
using Aurora.Framework.Modules;
using Aurora.Framework.Servers.HttpServer;
using Aurora.Framework.Servers.HttpServer.Implementation;
using Aurora.Framework.Services;
using Aurora.Framework.Services.ClassHelpers.Profile;
using Aurora.Framework.Utilities;
using Nini.Config;
using OpenMetaverse;
using System;
using System.Collections.Generic;

namespace Aurora.Modules.Web
{
    public class RegisterPage : IWebInterfacePage
    {
        public string[] FilePath
        {
            get
            {
                return new[]
                           {
                               "html/register.html"
                           };
            }
        }

        public bool RequiresAuthentication
        {
            get { return false; }
        }

        public bool RequiresAdminAuthentication
        {
            get { return false; }
        }

        public Dictionary<string, object> Fill(WebInterface webInterface, string filename, OSHttpRequest httpRequest,
                                               OSHttpResponse httpResponse, Dictionary<string, object> requestParameters,
                                               ITranslator translator, out string response)
        {
            response = null;
            var vars = new Dictionary<string, object>();

            if (requestParameters.ContainsKey("Submit"))
            {
                string AvatarName = requestParameters["AvatarName"].ToString();
                string AvatarPassword = requestParameters["AvatarPassword"].ToString();
                string FirstName = requestParameters["FirstName"].ToString();
                string LastName = requestParameters["LastName"].ToString();
                string UserAddress = requestParameters["UserAddress"].ToString();
                string UserZip = requestParameters["UserZip"].ToString();
                string UserCity = requestParameters["UserCity"].ToString();
                string UserEmail = requestParameters["UserEmail"].ToString();
                string UserDOBMonth = requestParameters["UserDOBMonth"].ToString();
                string UserDOBDay = requestParameters["UserDOBDay"].ToString();
                string UserDOBYear = requestParameters["UserDOBYear"].ToString();
                string AvatarArchive = requestParameters.ContainsKey("AvatarArchive")
                                           ? requestParameters["AvatarArchive"].ToString()
                                           : "";
                bool ToSAccept = requestParameters.ContainsKey("ToSAccept") &&
                                 requestParameters["ToSAccept"].ToString() == "Accepted";

                IGenericsConnector generics = Framework.Utilities.DataManager.RequestPlugin<IGenericsConnector>();
                var settings = generics.GetGeneric<GridSettings>(UUID.Zero, "WebSettings", "Settings");

                if (ToSAccept)
                {
                    AvatarPassword = Util.Md5Hash(AvatarPassword);

                    IUserAccountService accountService =
                        webInterface.Registry.RequestModuleInterface<IUserAccountService>();
                    UUID userID = UUID.Random();
                    string error = accountService.CreateUser(userID, settings.DefaultScopeID, AvatarName, AvatarPassword,
                                                             UserEmail);
                    if (error == "")
                    {
                        IAgentConnector con = Framework.Utilities.DataManager.RequestPlugin<IAgentConnector>();
                        con.CreateNewAgent(userID);
                        IAgentInfo agent = con.GetAgent(userID);
                        agent.OtherAgentInformation["RLFirstName"] = FirstName;
                        agent.OtherAgentInformation["RLLastName"] = LastName;
                        agent.OtherAgentInformation["RLAddress"] = UserAddress;
                        agent.OtherAgentInformation["RLCity"] = UserCity;
                        agent.OtherAgentInformation["RLZip"] = UserZip;
                        agent.OtherAgentInformation["UserDOBMonth"] = UserDOBMonth;
                        agent.OtherAgentInformation["UserDOBDay"] = UserDOBDay;
                        agent.OtherAgentInformation["UserDOBYear"] = UserDOBYear;
                        /*if (activationRequired)
                        {
                            UUID activationToken = UUID.Random();
                            agent.OtherAgentInformation["WebUIActivationToken"] = Util.Md5Hash(activationToken.ToString() + ":" + PasswordHash);
                            resp["WebUIActivationToken"] = activationToken;
                        }*/
                        con.UpdateAgent(agent);

                        if (AvatarArchive != "")
                        {
                            IProfileConnector profileData =
                                Framework.Utilities.DataManager.RequestPlugin<IProfileConnector>();
                            profileData.CreateNewProfile(userID);

                            IUserProfileInfo profile = profileData.GetUserProfile(userID);
                            profile.AArchiveName = AvatarArchive;
                            profile.IsNewUser = true;
                            profileData.UpdateUserProfile(profile);
                        }

                        response = "<h3>Successfully created account, redirecting to main page</h3>" +
                                   "<script language=\"javascript\">" +
                                   "setTimeout(function() {window.location.href = \"index.html\";}, 3000);" +
                                   "</script>";
                    }
                    else
                        response = "<h3>" + error + "</h3>";
                }
                else
                    response = "<h3>You did not accept the Terms of Service agreement.</h3>";
                return null;
            }

            List<Dictionary<string, object>> daysArgs = new List<Dictionary<string, object>>();
            for (int i = 1; i <= 31; i++)
                daysArgs.Add(new Dictionary<string, object> {{"Value", i}});

            List<Dictionary<string, object>> monthsArgs = new List<Dictionary<string, object>>();
            for (int i = 1; i <= 12; i++)
                monthsArgs.Add(new Dictionary<string, object> {{"Value", i}});

            List<Dictionary<string, object>> yearsArgs = new List<Dictionary<string, object>>();
            for (int i = 1900; i <= 2013; i++)
                yearsArgs.Add(new Dictionary<string, object> {{"Value", i}});

            vars.Add("Days", daysArgs);
            vars.Add("Months", monthsArgs);
            vars.Add("Years", yearsArgs);

            List<AvatarArchive> archives = webInterface.Registry.RequestModuleInterface<IAvatarAppearanceArchiver>().GetAvatarArchives();

            List<Dictionary<string, object>> avatarArchives = new List<Dictionary<string, object>>();
            IWebHttpTextureService webTextureService = webInterface.Registry.
                                                                    RequestModuleInterface<IWebHttpTextureService>();
            foreach (var archive in archives)
                avatarArchives.Add(new Dictionary<string, object>
                                       {
                                           {"AvatarArchiveName", archive.FileName },
                                           {"AvatarArchiveSnapshotID", archive.Snapshot},
                                           {
                                               "AvatarArchiveSnapshotURL",
                                               webTextureService.GetTextureURL(archive.Snapshot)
                                           }
                                       });

            vars.Add("AvatarArchive", avatarArchives);


            IConfig loginServerConfig =
                webInterface.Registry.RequestModuleInterface<ISimulationBase>().ConfigSource.Configs["LoginService"];
            string tosLocation = "";
            if (loginServerConfig != null && loginServerConfig.GetBoolean("UseTermsOfServiceOnFirstLogin", false))
                tosLocation = loginServerConfig.GetString("FileNameOfTOS", "");
            string ToS = "There are no Terms of Service currently. This may be changed at any point in the future.";

            if (tosLocation != "")
            {
                System.IO.StreamReader reader =
                    new System.IO.StreamReader(System.IO.Path.Combine(Environment.CurrentDirectory, tosLocation));
                ToS = reader.ReadToEnd();
                reader.Close();
            }
            vars.Add("ToSMessage", ToS);
            vars.Add("TermsOfServiceAccept", translator.GetTranslatedString("TermsOfServiceAccept"));
            vars.Add("TermsOfServiceText", translator.GetTranslatedString("TermsOfServiceText"));
            vars.Add("RegistrationsDisabled", translator.GetTranslatedString("RegistrationsDisabled"));
            vars.Add("RegistrationText", translator.GetTranslatedString("RegistrationText"));
            vars.Add("AvatarNameText", translator.GetTranslatedString("AvatarNameText"));
            vars.Add("AvatarPasswordText", translator.GetTranslatedString("Password"));
            vars.Add("AvatarPasswordConfirmationText", translator.GetTranslatedString("PasswordConfirmation"));
            vars.Add("AvatarScopeText", translator.GetTranslatedString("AvatarScopeText"));
            vars.Add("FirstNameText", translator.GetTranslatedString("FirstNameText"));
            vars.Add("LastNameText", translator.GetTranslatedString("LastNameText"));
            vars.Add("UserAddressText", translator.GetTranslatedString("UserAddressText"));
            vars.Add("UserZipText", translator.GetTranslatedString("UserZipText"));
            vars.Add("UserCityText", translator.GetTranslatedString("UserCityText"));
            vars.Add("UserCountryText", translator.GetTranslatedString("UserCountryText"));
            vars.Add("UserDOBText", translator.GetTranslatedString("UserDOBText"));
            vars.Add("UserEmailText", translator.GetTranslatedString("UserEmailText"));
            vars.Add("Accept", translator.GetTranslatedString("Accept"));
            vars.Add("Submit", translator.GetTranslatedString("Submit"));
            vars.Add("SubmitURL", "register.html");

            return vars;
        }

        public bool AttemptFindPage(string filename, ref OSHttpResponse httpResponse, out string text)
        {
            text = "";
            return false;
        }
    }
}