var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.DigitalWallet_Api>("api");

builder.Build().Run();
