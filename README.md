# SQL Server Bandwidth Test Tool

A .NET 9 console application that measures the bandwidth between your application and a Microsoft SQL Server by performing controlled data transfer tests in both upstream and downstream directions.

## Features

- Measures both upload and downstream bandwidth
- Configurable test parameters
- Progress tracking in 5% increments
- Supports Windows Authentication and SQL Authentication
- Real-time bandwidth reporting in MB/s and Mbps
- Automatic cleanup of test data

## Prerequisites

- .NET 9.0 SDK or later
- Access to a Microsoft SQL Server instance
- Appropriate SQL Server permissions to create temporary tables

## Configuration

Configure the application using either `appsettings.json` or command-line arguments:

{ "Server": "your_server_name", "Database": "your_database_name", "UserId": "your_username", "Password": "your_password", "IntegratedSecurity": "false", "TargetMB": "1000", "RowSize": "1000", "BatchSize": "1000" }

### Configuration Options

| Parameter | Description | Default |
|-----------|-------------|---------|
| Server | SQL Server instance name | required |
| Database | Target database name | required |
| UserId | SQL Server username | required if IntegratedSecurity is false |
| Password | SQL Server password | required if IntegratedSecurity is false |
| IntegratedSecurity | Use Windows Authentication | false |
| TargetMB | Target data size in megabytes | 1000 |
| RowSize | Size of each row in characters | 1000 |
| BatchSize | Number of rows per batch insert | 1000 |

## Usage

1. Build the project:

dotnet build

2. Run with configuration file:

dotnet run

3. Or override settings via command line:

dotnet run --Server="myserver" --Database="mydb" --IntegratedSecurity="true" --TargetMB="500"


## Output

The tool provides:
- Configuration verification
- Upload progress with real-time bandwidth measurements
- Download progress with real-time bandwidth measurements
- Final summary including:
  - Total rows and data size
  - Upload/Download times
  - Average bandwidth in MB/s and Mbps
  - Data verification results

## Performance Considerations

- Larger `BatchSize` values may improve upload performance but require more memory
- `RowSize` affects the amount of data per row
- `TargetMB` determines the total amount of data transferred
- Use `IntegratedSecurity` when possible for Windows Authentication

## Security Note

- Store sensitive connection information securely
- Never commit credentials to source control
- Use Windows Authentication (IntegratedSecurity) when possible
- Consider using environment variables for sensitive data

## License

[Your License Here]