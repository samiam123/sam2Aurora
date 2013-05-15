/*
 * Copyright (c) Contributors, http://aurora-sim.org/, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the Aurora-Sim Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using Aurora.Framework;
using Aurora.Framework.ClientInterfaces;
using Aurora.Framework.ConsoleFramework;
using Aurora.Framework.Modules;
using Aurora.Framework.PresenceInfo;
using Aurora.Framework.SceneInfo;
using Aurora.Framework.Services;
using Aurora.Framework.Services.ClassHelpers.Inventory;
using Nini.Config;
using OpenMetaverse;
using System;
using System.Collections.Generic;

namespace Aurora.Modules.Inventory
{
    public class InventoryTransferModule : INonSharedRegionModule
    {
        private IScene m_Scene;

        private bool m_Enabled = true;
        private IMessageTransferModule m_TransferModule;

        #region INonSharedRegionModule Members

        public void Initialise(IConfigSource config)
        {
            if (config.Configs["Messaging"] != null)
            {
                // Allow disabling this module in config
                //
                if (config.Configs["Messaging"].GetString(
                    "InventoryTransferModule", "InventoryTransferModule") !=
                    "InventoryTransferModule")
                {
                    m_Enabled = false;
                    return;
                }
            }
        }

        public void AddRegion(IScene scene)
        {
            if (!m_Enabled)
                return;

            m_Scene = scene;

            scene.RegisterModuleInterface(this);

            scene.EventManager.OnNewClient += OnNewClient;
            scene.EventManager.OnClosingClient += OnClosingClient;
            scene.EventManager.OnIncomingInstantMessage += OnGridInstantMessage;
        }

        public void RegionLoaded(IScene scene)
        {
            if (m_TransferModule == null)
            {
                m_TransferModule = m_Scene.RequestModuleInterface<IMessageTransferModule>();
                if (m_TransferModule == null)
                {
                    MainConsole.Instance.Error(
                        "[INVENTORY TRANSFER]: No Message transfer module found, transfers will be local only");
                    m_Enabled = false;

                    m_Scene = null;
                    scene.EventManager.OnNewClient -= OnNewClient;
                    scene.EventManager.OnClosingClient -= OnClosingClient;
                    scene.EventManager.OnIncomingInstantMessage -= OnGridInstantMessage;
                }
            }
        }

        public void RemoveRegion(IScene scene)
        {
            m_Scene = null;
            scene.EventManager.OnNewClient -= OnNewClient;
            scene.EventManager.OnClosingClient -= OnClosingClient;
            scene.EventManager.OnIncomingInstantMessage -= OnGridInstantMessage;
        }

        public void Close()
        {
        }

        public string Name
        {
            get { return "InventoryModule"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        #endregion

        private void OnNewClient(IClientAPI client)
        {
            // Inventory giving is conducted via instant message
            client.OnInstantMessage += OnInstantMessage;
        }

        private void OnClosingClient(IClientAPI client)
        {
            client.OnInstantMessage -= OnInstantMessage;
        }

        private void OnInstantMessage(IClientAPI client, GridInstantMessage im)
        {
            //MainConsole.Instance.InfoFormat("[INVENTORY TRANSFER]: OnInstantMessage {0}", im.dialog);

            if (im.Dialog == (byte) InstantMessageDialog.InventoryOffered)
            {
                //MainConsole.Instance.DebugFormat("Asset type {0}", ((AssetType)im.binaryBucket[0]));

                if (im.BinaryBucket.Length < 17) // Invalid
                    return;

                UUID receipientID = im.ToAgentID;
                IScenePresence user = m_Scene.GetScenePresence(receipientID);
                UUID copyID;

                // Send the IM to the recipient. The item is already
                // in their inventory, so it will not be lost if
                // they are offline.
                //
                if (user != null)
                {
                    // First byte is the asset type
                    AssetType assetType = (AssetType) im.BinaryBucket[0];

                    if (AssetType.Folder == assetType)
                    {
                        UUID folderID = new UUID(im.BinaryBucket, 1);

                        MainConsole.Instance.DebugFormat("[INVENTORY TRANSFER]: Inserting original folder {0} " +
                                                         "into agent {1}'s inventory",
                                                         folderID, im.ToAgentID);

                        m_Scene.InventoryService.GiveInventoryFolderAsync(receipientID, client.AgentId,
                                                                          folderID, UUID.Zero, (folder) =>
                                                                                                   {
                                                                                                       if (folder ==
                                                                                                           null)
                                                                                                       {
                                                                                                           client
                                                                                                               .SendAgentAlertMessage
                                                                                                               ("Can't find folder to give. Nothing given.",
                                                                                                                false);
                                                                                                           return;
                                                                                                       }

                                                                                                       // The outgoing binary bucket should contain only the byte which signals an asset folder is
                                                                                                       // being copied and the following bytes for the copied folder's UUID
                                                                                                       copyID =
                                                                                                           folder.ID;
                                                                                                       byte[]
                                                                                                           copyIDBytes =
                                                                                                               copyID
                                                                                                                   .GetBytes
                                                                                                                   ();
                                                                                                       im.BinaryBucket =
                                                                                                           new byte[
                                                                                                               1 +
                                                                                                               copyIDBytes
                                                                                                                   .Length
                                                                                                               ];
                                                                                                       im.BinaryBucket[0
                                                                                                           ] =
                                                                                                           (byte)
                                                                                                           AssetType
                                                                                                               .Folder;
                                                                                                       Array.Copy(
                                                                                                           copyIDBytes,
                                                                                                           0,
                                                                                                           im
                                                                                                               .BinaryBucket,
                                                                                                           1,
                                                                                                           copyIDBytes
                                                                                                               .Length);

                                                                                                       if (user != null)
                                                                                                           user
                                                                                                               .ControllingClient
                                                                                                               .SendBulkUpdateInventory
                                                                                                               (folder);

                                                                                                       im.SessionID =
                                                                                                           copyID;
                                                                                                       user
                                                                                                           .ControllingClient
                                                                                                           .SendInstantMessage
                                                                                                           (im);
                                                                                                   });
                    }
                    else
                    {
                        // First byte of the array is probably the item type
                        // Next 16 bytes are the UUID

                        UUID itemID = new UUID(im.BinaryBucket, 1);

                        MainConsole.Instance.DebugFormat("[INVENTORY TRANSFER]: (giving) Inserting item {0} " +
                                                         "into agent {1}'s inventory",
                                                         itemID, im.ToAgentID);

                        m_Scene.InventoryService.GiveInventoryItemAsync(
                            im.ToAgentID,
                            im.FromAgentID, itemID, UUID.Zero, false, (itemCopy) =>
                                                                          {
                                                                              if (itemCopy == null)
                                                                              {
                                                                                  client.SendAgentAlertMessage(
                                                                                      "Can't find item to give. Nothing given.",
                                                                                      false);
                                                                                  return;
                                                                              }

                                                                              copyID = itemCopy.ID;
                                                                              Array.Copy(copyID.GetBytes(), 0,
                                                                                         im.BinaryBucket, 1, 16);

                                                                              if (user != null)
                                                                              {
                                                                                  user.ControllingClient
                                                                                      .SendBulkUpdateInventory(itemCopy);
                                                                              }

                                                                              im.SessionID = itemCopy.ID;
                                                                              user.ControllingClient.SendInstantMessage(
                                                                                  im);
                                                                          });
                    }
                }
                else
                {
                    if (m_TransferModule != null)
                        m_TransferModule.SendInstantMessage(im);
                }
            }
            else if (im.Dialog == (byte) InstantMessageDialog.InventoryAccepted)
            {
                IScenePresence user = m_Scene.GetScenePresence(im.ToAgentID);

                if (user != null) // Local
                {
                    user.ControllingClient.SendInstantMessage(im);
                }
                else
                {
                    if (m_TransferModule != null)
                        m_TransferModule.SendInstantMessage(im);
                }
            }
            else if (im.Dialog == (byte) InstantMessageDialog.InventoryDeclined)
            {
                // Here, the recipient is local and we can assume that the
                // inventory is loaded. Courtesy of the above bulk update,
                // It will have been pushed to the client, too
                //
                IInventoryService invService = m_Scene.InventoryService;

                InventoryFolderBase trashFolder =
                    invService.GetFolderForType(client.AgentId, InventoryType.Unknown, AssetType.TrashFolder);

                UUID inventoryID = im.SessionID; // The inventory item/folder, back from it's trip

                InventoryItemBase item = invService.GetItem(client.AgentId, inventoryID);
                InventoryFolderBase folder = null;

                if (item != null && trashFolder != null)
                {
                    item.Folder = trashFolder.ID;

                    // Diva comment: can't we just update this item???
                    List<UUID> uuids = new List<UUID> {item.ID};
                    invService.DeleteItems(item.Owner, uuids);
                    ILLClientInventory inventory = client.Scene.RequestModuleInterface<ILLClientInventory>();
                    if (inventory != null)
                        inventory.AddInventoryItemAsync(client, item);
                }
                else
                {
                    folder = new InventoryFolderBase(inventoryID, client.AgentId);
                    folder = invService.GetFolder(folder);

                    if (folder != null & trashFolder != null)
                    {
                        folder.ParentID = trashFolder.ID;
                        invService.MoveFolder(folder);
                        client.SendBulkUpdateInventory(folder);
                    }
                }

                if ((null == item && null == folder) | null == trashFolder)
                {
                    string reason = String.Empty;

                    if (trashFolder == null)
                        reason += " Trash folder not found.";
                    if (item == null)
                        reason += " Item not found.";
                    if (folder == null)
                        reason += " Folder not found.";

                    client.SendAgentAlertMessage("Unable to delete " +
                                                 "received inventory" + reason, false);
                }

                IScenePresence user = m_Scene.GetScenePresence(im.ToAgentID);

                if (user != null) // Local
                {
                    user.ControllingClient.SendInstantMessage(im);
                }
                else
                {
                    if (m_TransferModule != null)
                        m_TransferModule.SendInstantMessage(im);
                }
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="msg"></param>
        private void OnGridInstantMessage(GridInstantMessage msg)
        {
            // Find agent to deliver to
            //
            IScenePresence user = m_Scene.GetScenePresence(msg.ToAgentID);

            // Just forward to local handling
            OnInstantMessage(user.ControllingClient, msg);
        }
    }
}