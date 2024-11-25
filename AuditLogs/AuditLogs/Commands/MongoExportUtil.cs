using System.IO;
using MongoDB.Bson;
using MongoDB.Driver;
using Sitecore.Shell.Framework.Commands;

namespace AuditLogs.Commands
{
    public class MongoExportUtil : Command
    {
        public override void Execute(CommandContext context)
        {
            Sitecore.Context.ClientPage.ClientResponse.Alert("MongoDB Export Started");
            MongoDBExportFuntion();
            Sitecore.Context.ClientPage.ClientResponse.Alert("MongoDB Export Ended");
        }


        public void MongoDBExportFuntion()
        {
            // MongoDB connection details
            //string connectionString = "mongodb://localhost:27017"; // Adjust if needed
            //string databaseName = "Audit";
            //string collectionName = "Logs";

            string connectionString = Sitecore.Configuration.Settings.GetSetting("MongoDBUrl"); // Adjust if needed
            string databaseName = Sitecore.Configuration.Settings.GetSetting("MongoDBName"); ;
            string collectionName = Sitecore.Configuration.Settings.GetSetting("MongoDBCollectionName"); ;

            // Initialize MongoDB client
            var client = new MongoClient(connectionString);
            var database = client.GetDatabase(databaseName);
            //var collection = database.GetCollection<BsonDocument>(collectionName);
            var collection = database.GetCollection<AuditLog>(collectionName);

            // Directory containing text files
            //string directoryPath = @"C:\inetpub\wwwroot\sc104sc.dev.local\App_Data\logs\audit\old";
            string directoryPath = Sitecore.Configuration.Settings.GetSetting(@"AuditLogDirectoryPath");

            // Get all text files from the directory
            var textFiles = Directory.GetFiles(directoryPath, "*.txt");

            foreach (var filePath in textFiles)
            {
                // Read file content
                string content = File.ReadAllText(filePath);

                var document = new AuditLog
                {
                    filename = Path.GetFileName(filePath),
                    content = content,
                };

                // Insert document into MongoDB
                //await collection.InsertOneAsync(document);

                var filter = Builders<AuditLog>.Filter
    .Eq(i => i.filename, document.filename);
                //to update specific field
                var update = Builders<AuditLog>.Update
    .Set(i => i.content, content);

                collection.UpdateOne(filter, update, new UpdateOptions { IsUpsert = true });
                Sitecore.Diagnostics.Log.Info($"Inserted file: {filePath}", this);
                File.Delete(filePath);
                //Console.WriteLine($"Inserted file: {filePath}");
            }
            Sitecore.Diagnostics.Log.Info("All files have been processed and inserted into MongoDB.", this);
            //Console.WriteLine("All files have been processed and inserted into MongoDB.");
        }
    }

    public class AuditLog
    {
        public ObjectId Id { get; set; }
        public string filename { get; set; }
        public string content { get; set; }
    }
}