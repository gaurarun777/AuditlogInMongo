using AuditLogs.Commands;
using MongoDB.Driver;
using Sitecore.Data.Events;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.Events;
using Sitecore.Pipelines;
using Sitecore.SecurityModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MongoDB.Bson;
using System.Text;

namespace AuditLogs.Diagnostics
{
    public class AuditLiveToMongo : Sitecore.Mvc.Pipelines.Loader.InitializeRoutes
    {
        #region Configuration Entries
        // The master switch for allowing auditing; To disable Sitecore item auditing add the following to <settings> section within a patch config: <setting name="Audit.Enabled" value="false"/>
        private static readonly bool _auditingEnabled = Sitecore.Configuration.Settings.GetBoolSetting("Audit.Enabled", true);

        // Boolean indication if this is a content authoring server (CAS) - which uses the master database.  Content delivery servers use the web database...
        private static readonly bool _isCAS = Sitecore.Sites.SiteManager.GetSite("website").Properties["database"].ToLower() == "master";

        // To prevent duplicate item names via the shell editor (UI) add the following to <settings> section within a patch config: <setting name="PreventDuplicateItemNames" value="true"/>
        // Both auditing, and auditing created items must be enabled...
        private static readonly bool _preventDuplicateNames = Sitecore.Configuration.Settings.GetBoolSetting("PreventDuplicateItemNames", false);

        // To disable auditing item creations, add the following to <settings> section within a patch config: <setting name="Audit.ItemCreating" value="false"/>
        private static readonly bool _auditItemCreating = Sitecore.Configuration.Settings.GetBoolSetting("Audit.ItemCreating", true);

        // To disable auditing changes to items, add the following to <settings> section within a patch config: <setting name="Audit.ItemSaving" value="false"/>
        private static readonly bool _auditItemSaving = Sitecore.Configuration.Settings.GetBoolSetting("Audit.ItemSaving", true);

        // To disable auditing item deletions, add the following to <settings> section within a patch config: <setting name="Audit.ItemDeleting" value="false"/>
        private static readonly bool _auditItemDeleting = Sitecore.Configuration.Settings.GetBoolSetting("Audit.ItemDeleting", true);

        // To disable auditing copying items, add the following to <settings> section within a patch config: <setting name="Audit.ItemCopying" value="false"/>
        private static readonly bool _auditItemCopying = Sitecore.Configuration.Settings.GetBoolSetting("Audit.ItemCopying", true);

        // To disable auditing item moves, add the following to <settings> section within a patch config: <setting name="Audit.ItemMoving" value="false"/>
        private static readonly bool _auditItemMoving = Sitecore.Configuration.Settings.GetBoolSetting("Audit.ItemMoving", true);

        // To disable auditing renaming of items, add the following to <settings> section within a patch config: <setting name="Audit.ItemRenamed" value="false"/>
        private static readonly bool _auditItemRenamed = Sitecore.Configuration.Settings.GetBoolSetting("Audit.ItemRenamed", true);

        // To enable auditing item sort order changes, add the following to <settings> section within a patch config: <setting name="Audit.ItemSortOrderChanged" value="true"/>
        private static readonly bool _auditItemSortOrderChanged = Sitecore.Configuration.Settings.GetBoolSetting("Audit.ItemSortOrderChanged", false);

        // To disable auditing item template changes, add the following to <settings> section within a patch config: <setting name="Audit.ItemTemplateChanged" value="false"/>
        private static readonly bool _auditItemTemplateChanged = Sitecore.Configuration.Settings.GetBoolSetting("Audit.ItemTemplateChanged", true);

        // To enable auditing item publish processed events, add the following to <settings> section within a patch config: <setting name="Audit.ItemPublished" value="true"/>
        private static readonly bool _auditItemPublished = Sitecore.Configuration.Settings.GetBoolSetting("Audit.ItemPublished", false);

        #endregion



        public override void Process(PipelineArgs args)
        {
            OnStart();
        }

        public void LiveLog(string message)
        {
            string time = DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss");
            string user = (Sitecore.Context.User == null) ? "extranet\\Anonymous" : Sitecore.Context.User.Name;
            if (Sitecore.Configuration.Settings.GetSetting("ExcludeItems").Split('|').Any(id => message.Contains(id)))
            {
                Sitecore.Diagnostics.Log.Info("This Item entry want to exclude", this);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    ExportToMongo(string.Format("({0}): ({1}): {2}", time, user, message));
                }
            }
        }

        private void ExportToMongo(string message)
        {
            string connectionString = Sitecore.Configuration.Settings.GetSetting("MongoDBUrl"); // Adjust if needed
            string databaseName = Sitecore.Configuration.Settings.GetSetting("MongoDBName"); ;
            string collectionName = Sitecore.Configuration.Settings.GetSetting("MongoDBCollectionName"); ;

            // Initialize MongoDB client
            var client = new MongoClient(connectionString);
            var database = client.GetDatabase(databaseName);
            var collection = database.GetCollection<BsonDocument>(collectionName);
            var document = new BsonDocument
            {
                { "content", message }
            };
            collection.InsertOne(document);
            Sitecore.Diagnostics.Log.Info("All files have been processed and inserted into MongoDB.", this);
        }

        public static void OnStart()
        {
            //if (_isCAS && _auditingEnabled)
            //{
            var handler = new AuditLiveToMongo();
            if (_auditItemCreating) Sitecore.Events.Event.Subscribe("item:creating", new EventHandler(handler.OnItemCreating));
            if (_auditItemSaving) Sitecore.Events.Event.Subscribe("item:saving", new EventHandler(handler.OnItemSaving));
            if (_auditItemDeleting) Sitecore.Events.Event.Subscribe("item:deleting", new EventHandler(handler.OnItemDeleting));

            if (_auditItemCopying) Sitecore.Events.Event.Subscribe("item:copying", new EventHandler(handler.OnItemCopying));
            if (_auditItemMoving) Sitecore.Events.Event.Subscribe("item:moving", new EventHandler(handler.OnItemMoving));
            if (_auditItemRenamed) Sitecore.Events.Event.Subscribe("item:renamed", new EventHandler(handler.OnItemRenamed));
            if (_auditItemSortOrderChanged) Sitecore.Events.Event.Subscribe("item:sortorderchanged", new EventHandler(handler.OnItemSortOrderChanged));
            if (_auditItemTemplateChanged) Sitecore.Events.Event.Subscribe("item:templateChanged", new EventHandler(handler.OnItemTemplateChanged));

            if (_auditItemPublished) Sitecore.Events.Event.Subscribe("publish:itemProcessed", new EventHandler(handler.OnItemPublished));

            //}

        }

        protected void OnItemPublished(object sender, EventArgs args)
        {
            StringBuilder logString = new StringBuilder();
            Assert.IsTrue(args != null, "args != null");
            if (args != null && args is Sitecore.Publishing.Pipelines.PublishItem.ItemProcessedEventArgs && _auditItemPublished)
            {
                using (new SecurityDisabler())
                {
                    Sitecore.Publishing.Pipelines.PublishItem.PublishItemContext context = (args as Sitecore.Publishing.Pipelines.PublishItem.ItemProcessedEventArgs).Context;

                    if (context.Result.Operation == Sitecore.Publishing.PublishOperation.Skipped)
                    {
                        try
                        {
                            // If we skipped publishing this item, we only care about logging why if we deliberately tried to republish this item...
                            if (!context.PublishOptions.CompareRevisions && context.PublishOptions.RootItem.ID == context.ItemId)
                            {
                                if (context.PublishHelper.SourceItemExists(context.ItemId))
                                {
                                    Item sourceItem = context.PublishHelper.GetSourceItem(context.ItemId);
                                    logString.Append(string.Format("PUBLISH [{0}]: {1}", context.Result.Operation, AuditFormatter.FormatItem(sourceItem)));
                                }
                                else
                                {
                                    logString.AppendLine(string.Format("PUBLISH [{0}]: {1}", context.Result.Operation, context.ItemId.ToString()));
                                }
                                logString.AppendLine(string.Format("** {0}", context.Result.Explanation));
                            }
                            LiveLog(logString.ToString());
                        }
                        catch (Exception)
                        {
                            // We don't need to log - we were skipping this item from getting published anyway
                        }
                    }
                    else
                    {
                        if (context.PublishHelper.SourceItemExists(context.ItemId))
                        {
                            Item sourceItem = context.PublishHelper.GetSourceItem(context.ItemId);
                            logString.AppendLine(string.Format("PUBLISH [{0}]: {1}", context.Result.Operation, AuditFormatter.FormatItem(sourceItem)));
                        }
                        else
                        {
                            logString.AppendLine(string.Format("PUBLISH [{0}]: {1}, msg: {2}", context.Result.Operation, context.ItemId.ToString(), context.Result.Explanation));
                        }
                        LiveLog(logString.ToString());
                    }

                }
            }
        }

        /// <summary>
        /// Responds to Sitecore new item creation, cloning an item, and duplicating an item (either via the UI or API)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args">Param index 0 contains the ItemCreatingEventArgs: Contains item ID, name, master and template IDs, parent item</param>
        protected void OnItemCreating(object sender, EventArgs args)
        {
            StringBuilder logString = new StringBuilder();
            Assert.IsTrue(args != null, "args != null");
            if (args != null && _auditItemCreating)
            {
                using (new SecurityDisabler())
                {
                    ItemCreatingEventArgs arg = Event.ExtractParameter(args, 0) as ItemCreatingEventArgs;
                    Assert.IsTrue(arg != null, "arg != null");

                    if ((arg != null) && (Sitecore.Context.Site.Name == "shell") && (_preventDuplicateNames))
                    {
                        foreach (Item currentItem in arg.Parent.GetChildren())
                        {
                            if ((arg.ItemName.Replace(' ', '-').ToLower() == currentItem.Name.ToLower()) && (arg.ItemId != currentItem.ID))
                            {
                                arg.Cancel = true;
                                Sitecore.Context.ClientPage.ClientResponse.Alert("Name \"" + currentItem.Name + "\" is already in use. Please use another name for the item.");
                                return;
                            }
                        }
                    }

                    if (arg != null && ShouldAudit(arg.Parent))
                    {
                        Item t = arg.Parent.Database.Items[arg.TemplateId];
                        string templateName = t != null ? t.Name : arg.TemplateId.ToString();

                        logString.AppendLine(string.Format("CREATE: {0}:{1}/{2}, id: {3}, template: {4}", arg.Parent.Database.Name, arg.Parent.Paths.Path, arg.ItemName, arg.ItemId.ToString(), templateName));
                        LiveLog(logString.ToString());
                    }
                }
            }
        }

        /// <summary>
        /// Responds to Sitecore item deletions
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args">Param index 0 contains the Item being deleted</param>
        protected void OnItemDeleting(object sender, EventArgs args)
        {
            StringBuilder logString = new StringBuilder();
            Assert.IsTrue(args != null, "args != null");
            if (args != null && _auditItemDeleting)
            {
                using (new SecurityDisabler())
                {
                    Item item = Event.ExtractParameter(args, 0) as Item;
                    Assert.IsTrue(item != null, "item != null");

                    if (item != null && ShouldAudit(item))
                    {
                        logString.AppendLine(string.Format("DELETE: {0}", AuditFormatter.FormatItem(item)));
                        LiveLog(logString.ToString());
                    }
                }
            }
        }

        /// <summary>
        /// Responds to Sitecore item updates
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args">Param index 0 contains the Item being saved</param>
        protected void OnItemSaving(object sender, EventArgs args)
        {
            StringBuilder logString = new StringBuilder();
            Assert.IsTrue(args != null, "args != null");
            if (args != null && _auditItemSaving)
            {
                using (new SecurityDisabler())
                {
                    Item item = Event.ExtractParameter(args, 0) as Item;
                    Assert.IsTrue(item != null, "item != null");

                    if (item != null && ShouldAudit(item))
                    {
                        Item originalItem = item.Database.GetItem(item.ID, item.Language, item.Version);

                        var differences = FindDifferences(item, originalItem);

                        if (differences.Any())
                        {
                            TimeSpan createdTS = item.Statistics.Updated - item.Statistics.Created;
                            TimeSpan sinceLastSave = item.Statistics.Updated - originalItem.Statistics.Updated;

                            if (createdTS.TotalSeconds > 2 && sinceLastSave.TotalSeconds > 2)
                            {
                                logString.AppendLine(string.Format("SAVE: {0}", AuditFormatter.FormatItem(item)));
                            }

                            foreach (string f in differences)
                            {
                                if (string.IsNullOrWhiteSpace(originalItem[f]))
                                {
                                    logString.AppendLine(string.Format("** [{0}]: {1}", item.Fields[f].DisplayName, item[f]));
                                }
                                else if (originalItem[f].Length <= 40 && item[f].Length <= 40)
                                {
                                    logString.AppendLine(string.Format("** [{0}]: {1}, old: {2}", item.Fields[f].DisplayName, item[f], originalItem[f]));
                                }
                                else
                                {
                                    logString.AppendLine(string.Format("** [{0}]", item.Fields[f].DisplayName));
                                    logString.AppendLine(string.Format("   -> New: {0}", item[f]));
                                    logString.AppendLine(string.Format("   -> Old: {0}", originalItem[f]));
                                }
                            }
                        }
                    }
                }
            }
            LiveLog(logString.ToString());
        }

        /// <summary>
        /// Find non-system fields that have changed
        /// </summary>
        /// <param name="newItem"></param>
        /// <param name="originalItem"></param>
        /// <returns></returns>
        private static List<string> FindDifferences(Item newItem, Item originalItem)
        {
            newItem.Fields.ReadAll();

            IEnumerable<string> fieldNames = newItem.Fields.Select(f => f.Name).Where(name => !name.StartsWith("__"));

            return fieldNames
              .Where(fieldName => newItem[fieldName] != originalItem[fieldName] && originalItem.Fields[fieldName] != null && newItem.Fields[fieldName].ID == originalItem.Fields[fieldName].ID)
              .ToList();
        }

        private static bool ShouldAudit(Item item)
        {
            return item.Database.Name.ToLower() == "master";
        }

        /// <summary>
        /// Responds to Sitecore item copying
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args">
        /// Param index 0 contains the Item being copied,
        /// Param index 1 contains the Item Copy destination,
        /// Param index 2 contains the Result item name,
        /// Param index 3 contains the Result item ID,
        /// Param index 4 contains the boolean indication whether it is a recursive copy (including children) or not
        /// </param>
        protected void OnItemCopying(object sender, EventArgs args)
        {
            StringBuilder logString = new StringBuilder();
            Assert.IsTrue(args != null, "args != null");
            if (args != null && _auditItemCopying)
            {
                using (new SecurityDisabler())
                {
                    Item item = Event.ExtractParameter(args, 0) as Item;
                    Assert.IsTrue(item != null, "item != null");

                    if (item != null && ShouldAudit(item))
                    {
                        Item destination = Event.ExtractParameter(args, 1) as Item;
                        string itemName = Event.ExtractParameter(args, 2) as string;
                        Sitecore.Data.ID itemID = Event.ExtractParameter(args, 3) as Sitecore.Data.ID;
                        bool recursive = (bool)Event.ExtractParameter(args, 4);

                        if (item.Parent.Paths.Path == destination.Paths.Path && item.Name != itemName)
                        {
                            logString.AppendLine(string.Format("DUPLICATE: {0}:{1}, destination: {2}/{3}, id: {4}{5}", item.Database.Name, item.Paths.Path, destination.Paths.Path, itemName, itemID.ToString(), item.Children.Count == 0 ? string.Empty : string.Format(" recursive: {0}", recursive.ToString())));
                        }

                        else
                        {
                            logString.AppendLine(string.Format("COPY: {0}:{1}, destination: {2}/{3}, id: {4}{5}", item.Database.Name, item.Paths.Path, destination.Paths.Path, itemName, itemID.ToString(), item.Children.Count == 0 ? string.Empty : string.Format(" recursive: {0}", recursive.ToString())));
                        }
                        LiveLog(logString.ToString());
                    }
                }
            }
        }

        /// <summary>
        /// Responds to Sitecore item moving
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args">
        /// Param index 0 contains the Item being moved,
        /// Param index 1 contains the ID of the old parent,
        /// Param index 2 contains the ID of the new parent
        /// </param>
        protected void OnItemMoving(object sender, EventArgs args)
        {
            StringBuilder logString = new StringBuilder();
            Assert.IsTrue(args != null, "args != null");
            if (args != null && _auditItemMoving)
            {
                using (new SecurityDisabler())
                {
                    Item item = Event.ExtractParameter(args, 0) as Item;
                    Assert.IsTrue(item != null, "item != null");

                    if (ShouldAudit(item))
                    {
                        Sitecore.Data.ID oldParentID = Event.ExtractParameter(args, 1) as Sitecore.Data.ID;
                        Sitecore.Data.ID newParentID = Event.ExtractParameter(args, 2) as Sitecore.Data.ID;
                        Item oldParent = item.Database.Items[oldParentID];
                        Item newParent = item.Database.Items[newParentID];

                        if (item != null && oldParent != null && newParent != null && oldParent.ID != newParent.ID)
                        {
                            logString.AppendLine(string.Format("MOVE: [{0}] from: {1}:{2} to: {3}:{4}", item.Name, oldParent.Database.Name, oldParent.Paths.Path, newParent.Database.Name, newParent.Paths.Path));
                            LiveLog(logString.ToString());
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Responds to Sitecore item rename
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args">
        /// Param index 0 contains the result Item,
        /// Param index 1 contains the Item name prior to being renamed
        /// </param>
        protected void OnItemRenamed(object sender, EventArgs args)
        {
            StringBuilder logString = new StringBuilder();
            Assert.IsTrue(args != null, "args != null");
            if (args != null && _auditItemRenamed)
            {
                using (new SecurityDisabler())
                {
                    Item item = Event.ExtractParameter(args, 0) as Item;
                    string itemNameBeforeRename = Event.ExtractParameter(args, 1) as string;

                    Assert.IsTrue(item != null, "item != null");

                    if (item != null && itemNameBeforeRename != item.Name && ShouldAudit(item))
                    {
                        logString.AppendLine(string.Format("RENAME: {0}:{1}/{2}, as: {3}", item.Database.Name, item.Parent.Paths.Path, itemNameBeforeRename, item.Name));
                        LiveLog(logString.ToString());
                    }
                }
            }
        }

        /// <summary>
        /// Responds to Sitecore item sort order changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args">
        /// Param index 0 contains the sorted Item,
        /// Param index 1 contains the Old sortorder value (string)
        /// </param>
        protected void OnItemSortOrderChanged(object sender, EventArgs args)
        {
            StringBuilder logString = new StringBuilder();
            Assert.IsTrue(args != null, "args != null");
            if (args != null && _auditItemSortOrderChanged)
            {
                using (new SecurityDisabler())
                {
                    Item item = Event.ExtractParameter(args, 0) as Item;
                    string oldSortOrder = Event.ExtractParameter(args, 1) as string;

                    Assert.IsTrue(item != null, "item != null");

                    if (item != null && ShouldAudit(item))
                    {
                        logString.AppendLine(string.Format("SORT: {0}:{1}, new: {2}, old: {3}", item.Database.Name, item.Paths.Path, item.Appearance.Sortorder, oldSortOrder));
                        LiveLog(logString.ToString());
                    }
                }
            }
        }

        /// <summary>
        /// Responds to Sitecore item template changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args">
        /// Param index 0 contains the ID of the item being changed,
        /// Param index 1 contains the Instance of the datamanager class handling the template
        /// </param>
        protected void OnItemTemplateChanged(object sender, EventArgs args)
        {
            StringBuilder logString = new StringBuilder();
            Assert.IsTrue(args != null, "args != null");
            if (args != null && _auditItemDeleting)
            {
                using (new SecurityDisabler())
                {
                    Item item = Event.ExtractParameter(args, 0) as Item;
                    Sitecore.Data.Templates.TemplateChangeList change = Event.ExtractParameter(args, 1) as Sitecore.Data.Templates.TemplateChangeList;

                    Assert.IsTrue(item != null, "item != null");

                    if (item != null && ShouldAudit(item) && change.Target.ID != change.Source.ID)
                    {
                        logString.AppendLine(string.Format("TEMPLATE CHANGE: {0}:{1}, target: {2}, source: {3}", item.Database.Name, item.Paths.Path, change.Target.Name, change.Source.Name));
                        foreach (Sitecore.Data.Templates.TemplateChangeList.TemplateChange c in change.Changes)
                        {
                            if (c.Action == Sitecore.Data.Templates.TemplateChangeAction.DeleteField)
                            {
                                logString.AppendLine(string.Format("** {0}: {1}", c.Action, c.SourceField.Name));
                            }
                        }
                    }
                    LiveLog(logString.ToString());
                }
            }
        }
    }
}