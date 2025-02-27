using System;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.Text;


namespace SqlBenchmark
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Build configuration
            IConfiguration config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true)
                .AddCommandLine(args)
                .Build();

            // Get configuration values
            string server = config["Server"]!;
            string database = config["Database"]!;
            string userId = config["UserId"]!;
            string password = config["Password"]!;
            int targetMB = int.Parse(config["TargetMB"] ?? "1000"); // Default to 1000 MB if not specified
            bool integratedSecurity = bool.Parse(config["IntegratedSecurity"] ?? "false"); // Default to false
            int rowSize = int.Parse(config["RowSize"] ?? "1000"); // Default to 1000 characters per row
            int batchSize = int.Parse(config["BatchSize"] ?? "1000"); // Default to 1000 inserts per batch

            // Echo configuration to the Terminal
            Console.WriteLine("Current Configuration:");
            Console.WriteLine($"Server: {server}");
            Console.WriteLine($"Database: {database}");
            Console.WriteLine($"Integrated Security: {integratedSecurity}");
            Console.WriteLine($"Target MB: {targetMB}");
            Console.WriteLine($"Row Size: {rowSize}");
            Console.WriteLine($"Batch Size: {batchSize}");
            Console.WriteLine();

            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(database) ||
                (!integratedSecurity && (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(password))))
            {
                Console.WriteLine("Please provide Server and Database. Provide UserId and Password unless using IntegratedSecurity.");
                return;
            }

            // Use SqlConnectionStringBuilder to construct and verify the connection string
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = database,
                IntegratedSecurity = integratedSecurity,
                TrustServerCertificate = true,
                ApplicationName = "MyBandwidthTest"
            };

            if (!integratedSecurity)
            {
                builder.UserID = userId;
                builder.Password = password;
            }

            string connectionString = builder.ConnectionString;

            try
            {
                Console.WriteLine("Verifying connection string...");
                using (SqlConnection verifyConnection = new SqlConnection(connectionString))
                {
                    await verifyConnection.OpenAsync();
                    Console.WriteLine("Connection successful.");
                }

                Console.WriteLine("Starting bandwidth test...");

                Stopwatch totalStopwatch = new Stopwatch();
                totalStopwatch.Start();

                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    // Create temporary table
                    string createTableCmd = @"
                                            IF OBJECT_ID('tempdb..#TempData') IS NOT NULL
                                                DROP TABLE #TempData;
                                            CREATE TABLE #TempData (
                                                Id INT IDENTITY(1,1) PRIMARY KEY,
                                                Data NVARCHAR(MAX)
                                            );";
                    using (SqlCommand cmd = new SqlCommand(createTableCmd, connection))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }

                    // Insert data to reach target MB using batched insert statements with table value constructor
                    long targetBytes = targetMB * 1024L * 1024L;
                    long insertedBytes = 0;
                    string staticData = new string('A', rowSize);
                    int totalMilestones = 20; // 5% increments
                    long milestoneIncrement = targetBytes / totalMilestones;
                    long nextUploadMilestone = milestoneIncrement;
                    double lastUploadElapsedSeconds = 0;

                    Stopwatch upstreamStopwatch = new Stopwatch();
                    upstreamStopwatch.Start();

                    while (insertedBytes < targetBytes)
                    {
                        StringBuilder batchInsertBuilder = new StringBuilder();
                        batchInsertBuilder.Append("INSERT INTO #TempData (Data) VALUES ");

                        int currentBatchSize = 0;
                        while (currentBatchSize < batchSize && insertedBytes < targetBytes)
                        {
                            batchInsertBuilder.Append($"('{staticData}'), ");
                            insertedBytes += rowSize * 2; // NVARCHAR uses 2 bytes per character
                            currentBatchSize++;

                            // Check for upload milestones
                            if (insertedBytes >= nextUploadMilestone && nextUploadMilestone <= targetBytes)
                            {
                                double currentElapsedSeconds = upstreamStopwatch.Elapsed.TotalSeconds;
                                double intervalSeconds = currentElapsedSeconds - lastUploadElapsedSeconds;
                                double megabits = (milestoneIncrement * 8) / 1_000_000.0;
                                double uploadMbps = intervalSeconds > 0 ? megabits / intervalSeconds : 0;

                                double progress = (double)nextUploadMilestone / targetBytes * 100;
                                Console.WriteLine($"Upload Progress: {progress:F0}% ({nextUploadMilestone / (1024 * 1024)} MB) - Bandwidth: {uploadMbps:F2} Mbps");

                                lastUploadElapsedSeconds = currentElapsedSeconds;
                                nextUploadMilestone += milestoneIncrement;
                            }
                        }

                        // Remove the last comma and space, then add a semicolon
                        if (batchInsertBuilder.Length > 0)
                        {
                            batchInsertBuilder.Length -= 2; // Remove last ", "
                            batchInsertBuilder.Append(";");
                        }

                        string batchInsertCmdText = batchInsertBuilder.ToString();

                        using (SqlCommand insertCmd = new SqlCommand(batchInsertCmdText, connection))
                        {
                            await insertCmd.ExecuteNonQueryAsync();
                        }
                    }

                    upstreamStopwatch.Stop();
                    double upstreamElapsedSecondsTotal = upstreamStopwatch.Elapsed.TotalSeconds;
                    double upstreamMB = insertedBytes / (1024.0 * 1024.0);
                    double upstreamMbTotal = upstreamMB * 8;
                    double upstreamBandwidthMBps = upstreamMB / upstreamElapsedSecondsTotal;
                    double upstreamBandwidthMbps = upstreamMbTotal / upstreamElapsedSecondsTotal;

                    // Select and verify data
                    Stopwatch downstreamStopwatch = new Stopwatch();
                    downstreamStopwatch.Start();

                    string selectCmdText = "SELECT * FROM #TempData;";
                    long totalDataBytes = 0;
                    int rowCount = 0;
                    long nextDownloadMilestone = milestoneIncrement;
                    double lastDownloadElapsedSeconds = 0;

                    using (SqlCommand selectCmd = new SqlCommand(selectCmdText, connection))
                    using (SqlDataReader reader = await selectCmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            rowCount++;
                            string data = reader.GetString(1);
                            totalDataBytes += data.Length * 2; // NVARCHAR uses 2 bytes per character

                            // Check for download milestones
                            if (totalDataBytes >= nextDownloadMilestone && nextDownloadMilestone <= targetBytes)
                            {
                                double currentElapsedSeconds = downstreamStopwatch.Elapsed.TotalSeconds;
                                double intervalSeconds = currentElapsedSeconds - lastDownloadElapsedSeconds;
                                double megabits = (milestoneIncrement * 8) / 1_000_000.0;
                                double downloadMbps = intervalSeconds > 0 ? megabits / intervalSeconds : 0;

                                double progress = (double)nextDownloadMilestone / targetBytes * 100;
                                Console.WriteLine($"Download Progress: {progress:F0}% ({nextDownloadMilestone / (1024 * 1024)} MB) - Bandwidth: {downloadMbps:F2} Mbps");

                                lastDownloadElapsedSeconds = currentElapsedSeconds;
                                nextDownloadMilestone += milestoneIncrement;
                            }
                        }
                    }

                    downstreamStopwatch.Stop();
                    double downstreamElapsedSecondsTotal = downstreamStopwatch.Elapsed.TotalSeconds;
                    double totalDataMB = totalDataBytes / (1024.0 * 1024.0);
                    double totalDataMb = totalDataBytes * 8 / 1_000_000.0;
                    double downstreamBandwidthMBps = totalDataMB / downstreamElapsedSecondsTotal;
                    double downstreamBandwidthMbps = totalDataMb / downstreamElapsedSecondsTotal;

                    Console.WriteLine($"Inserted {rowCount} rows totaling {totalDataMB:F2} MB ({totalDataMb:F2} Mb).");
                    Console.WriteLine($"Target was {targetMB} MB.");
                    Console.WriteLine($"Upstream Elapsed Time: {upstreamElapsedSecondsTotal:F2} seconds.");
                    Console.WriteLine($"Upstream Bandwidth: {upstreamBandwidthMBps:F2} MB/s ({upstreamBandwidthMbps:F2} Mbps)");
                    Console.WriteLine($"Downstream Elapsed Time: {downstreamElapsedSecondsTotal:F2} seconds.");
                    Console.WriteLine($"Downstream Bandwidth: {downstreamBandwidthMBps:F2} MB/s ({downstreamBandwidthMbps:F2} Mbps)");

                    if (Math.Abs(totalDataMB - targetMB) < 1)
                    {
                        Console.WriteLine("Data verification successful.");
                    }
                    else
                    {
                        Console.WriteLine("Data verification failed.");
                    }

                    // Drop temporary table
                    string dropTableCmd = "DROP TABLE #TempData;";
                    using (SqlCommand cmd = new SqlCommand(dropTableCmd, connection))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                totalStopwatch.Stop();
                Console.WriteLine($"Bandwidth test completed in {totalStopwatch.Elapsed.TotalSeconds:F2} seconds.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}
/*
The code is a console application that uses the  Microsoft.Data.SqlClient  library to connect to a SQL Server database and perform a bandwidth test. The test involves inserting data into a temporary table until a target size is reached, then reading the data back to verify the size and calculate the bandwidth. 
 The code reads configuration values from  appsettings.json  or command-line arguments, constructs a connection string, and verifies the connection. It then creates a temporary table, inserts data in batches, and reads the data back. The code calculates the bandwidth for both the upload and download operations and outputs the results to the console. 
 The code uses a  SqlConnectionStringBuilder  to construct the connection string and  SqlCommand  to execute SQL commands. It also uses  Stopwatch  to measure the elapsed time for the operations. 
 The code is structured as a single  Main  method that reads the configuration, performs the bandwidth test, and outputs the results. It handles exceptions and outputs error messages if any occur. 
 The code is well-commented and includes error handling to catch and report exceptions. It also includes configuration validation and output of the configuration values to the console. 
 The code uses asynchronous methods to perform database operations, which can improve performance by allowing other operations to run while waiting for I/O operations to complete. 
 The code uses string interpolation to construct SQL commands and output messages, which can improve readability and maintainability. 
 The code uses the  StringBuilder  class to construct batched insert statements, which can improve performance by reducing memory allocations and string concatenation overhead. 
 The code uses the  SqlDataReader  class to read data from the database, which provides a forward-only, read-only stream of data. 
 The code uses the  DROP TABLE  command to clean up the temporary table after the test is complete, which is a good practice to avoid leaving temporary objects in the database. 
 The code uses the  IF OBJECT_ID  command to check if the temporary table exists before dropping it, which is a good practice to avoid errors if the table does not exist. 
 The code uses the  PRIMARY KEY  constraint on the  Id  column of the temporary table, which enforces uniqueness and can improve query performance. 
 The code uses the  NVARCHAR(MAX)  data type for the  Data  column of the temporary table, which allows storing large amounts of Unicode text data. 
 The code uses the  IDENTITY  property on the  Id  column of the temporary

*/