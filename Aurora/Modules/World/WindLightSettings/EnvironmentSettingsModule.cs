﻿using Aurora.Framework;
using Aurora.Framework.ClientInterfaces;
using Aurora.Framework.Modules;
using Aurora.Framework.PresenceInfo;
using Aurora.Framework.SceneInfo;
using Aurora.Framework.Servers.HttpServer;
using Aurora.Framework.Servers.HttpServer.Implementation;
using Aurora.Framework.Servers.HttpServer.Interfaces;
using Aurora.Framework.Services;
using Aurora.Framework.Utilities;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using System;
using System.Collections.Generic;
using System.IO;

namespace Aurora.Modules
{
    public class EnvironmentSettingsModule : IEnvironmentSettingsModule, INonSharedRegionModule
    {
        private IScene m_scene;

        #region INonSharedRegionModule Members

        public void Initialise(IConfigSource source)
        {
        }

        public void AddRegion(IScene scene)
        {
            m_scene = scene;
            m_scene.RegisterModuleInterface<IEnvironmentSettingsModule>(this);
            scene.EventManager.OnRegisterCaps += EventManager_OnRegisterCaps;
        }

        public void RegionLoaded(IScene scene)
        {
        }

        public void RemoveRegion(IScene scene)
        {
        }

        public void Close()
        {
        }

        public string Name
        {
            get { return "EnvironmentSettingsModule"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        #endregion

        private OSDMap EventManager_OnRegisterCaps(UUID agentID, IHttpServer server)
        {
            OSDMap retVal = new OSDMap();
            retVal["EnvironmentSettings"] = CapsUtil.CreateCAPS("EnvironmentSettings", "");
            //Sets the windlight settings
            server.AddStreamHandler(new GenericStreamHandler("POST", retVal["EnvironmentSettings"],
                                                             delegate(string path, Stream request,
                                                                      OSHttpRequest httpRequest,
                                                                      OSHttpResponse httpResponse)
                                                                 { return SetEnvironment(request, agentID); }));
            //Sets the windlight settings
            server.AddStreamHandler(new GenericStreamHandler("GET", retVal["EnvironmentSettings"],
                                                             delegate(string path, Stream request,
                                                                      OSHttpRequest httpRequest,
                                                                      OSHttpResponse httpResponse)
                                                                 { return EnvironmentSettings(agentID); }));
            return retVal;
        }

        private byte[] SetEnvironment(Stream request, UUID agentID)
        {
            IScenePresence SP = m_scene.GetScenePresence(agentID);
            if (SP == null)
                return new byte[0]; //They don't exist
            bool success = false;
            string fail_reason = "";
            if (SP.Scene.Permissions.CanIssueEstateCommand(agentID, false))
            {
                m_scene.RegionInfo.EnvironmentSettings = OSDParser.DeserializeLLSDXml(HttpServerHandlerHelpers.ReadFully(request));
                success = true;

                //Tell everyone about the changes
                TriggerWindlightUpdate(1);
            }
            else
            {
                fail_reason = "You don't have permissions to set the windlight settings here.";
                SP.ControllingClient.SendAlertMessage(
                    "You don't have the correct permissions to set the Windlight Settings");
            }
            OSDMap result = new OSDMap()
                                {
                                    new KeyValuePair<string, OSD>("success", success),
                                    new KeyValuePair<string, OSD>("regionID", SP.Scene.RegionInfo.RegionID)
                                };
            if (fail_reason != "")
                result["fail_reason"] = fail_reason;

            return OSDParser.SerializeLLSDXmlBytes(result);
        }

        private byte[] EnvironmentSettings(UUID agentID)
        {
            IScenePresence SP = m_scene.GetScenePresence(agentID);
            if (SP == null)
                return new byte[0]; //They don't exist

            if (m_scene.RegionInfo.EnvironmentSettings != null)
                return OSDParser.SerializeLLSDXmlBytes(m_scene.RegionInfo.EnvironmentSettings);
            return new byte[0];
        }

        public void TriggerWindlightUpdate(int interpolate)
        {
            foreach (IScenePresence presence in m_scene.GetScenePresences())
            {
                OSD item = BuildEQM(interpolate);
                IEventQueueService eq = presence.Scene.RequestModuleInterface<IEventQueueService>();
                if (eq != null)
                    eq.Enqueue(item, presence.UUID, presence.Scene.RegionInfo.RegionID);
            }
        }

        private OSD BuildEQM(int interpolate)
        {
            OSDMap map = new OSDMap();

            OSDMap body = new OSDMap();

            body.Add("Interpolate", interpolate);

            map.Add("body", body);
            map.Add("message", OSD.FromString("WindLightRefresh"));
            return map;
        }

        public WindlightDayCycle GetCurrentDayCycle()
        {
            if (m_scene.RegionInfo.EnvironmentSettings != null)
            {
                WindlightDayCycle cycle = new WindlightDayCycle();
                cycle.FromOSD(m_scene.RegionInfo.EnvironmentSettings);
                return cycle;
            }
            return null;
        }

        public void SetDayCycle(WindlightDayCycle cycle)
        {
            m_scene.RegionInfo.EnvironmentSettings = cycle.ToOSD();
        }
    }
}