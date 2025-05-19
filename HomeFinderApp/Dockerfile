# Use the official .NET SDK image for build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore as distinct layers
COPY HomeFinderApp.csproj ./
RUN dotnet restore

# Copy the rest of the source code
COPY . ./

# Publish the app to the /app/publish directory
RUN dotnet publish -c Release -o /app/publish

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Expose port 80 for Container Apps
EXPOSE 80

# Set ASP.NET Core to listen on port 80
ENV ASPNETCORE_URLS=http://+:80

ENTRYPOINT ["dotnet", "HomeFinderApp.dll"]