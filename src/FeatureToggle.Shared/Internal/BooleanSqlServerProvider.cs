
using System;
using System.Configuration;
using System.Data.SqlClient;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Configuration;

namespace FeatureToggle.Internal
{
    public class BooleanSqlServerProvider : IBooleanToggleValueProvider
    {
        private const string AppSettingsKeyValuesIsEmptyErrorMessage = "The <appSettings> value for key '{0}' is empty.";
        private const string NamedConnectionStringsValueIsEmptyErrorMessage = "The <connectionStrings> value for connection named '{0}' is empty.";
        private const string CouldNotFindValueForMessage = "Could not find value for '{0}'.";
        private const string TheAppSettingsKeyWasNotFound = "The appSettings key '{0}' was not found.";
#if NETFULL

        public bool EvaluateBooleanToggleValue(IFeatureToggle toggle)
        {
            var connectionString = GetConnectionStringFromConfig(toggle);
            var sqlCommandText = GetCommandTextFromAppConfig(toggle);

            return RunQuery(connectionString, sqlCommandText);
        }


#if NETCORE

        /// <summary>
        ///  "FeatureToggle": {
        ///     "SomeSqlServerToggle" : {
        ///         "ConnectionString" : "Data Source=.\SQLEXPRESS;Initial Catalog=FeatureToggleIntegrationTests;Integrated Security=True;Pooling=False"" 
        ///         "SqlStatement" : "SELECT select Value from Toggles where ToggleName = 'MySqlServerToggleFalse'"
        ///     }
        ///  },
        ///  
        /// </summary>
        /// <param name="configuration"></param>
        /// <param name="toggle"></param>
        /// <returns></returns>
        public bool EvaluateBooleanToggleValue(IFeatureToggle toggle)
        {
            IConfigurationRoot configuration = GetConfiguration();

            var connectionString = GetConnectionStringFromAppSettings(toggle);
            var sqlCommandText = GetCommandTextFromAppSettings(toggle);

            return RunQuery(connectionString, sqlCommandText);
        }


        private IConfigurationRoot GetConfiguration()
        {
            const string appSettings = "appsettings";
            const string json = "json";
            string environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile($"{appSettings}.{json}", optional: false, reloadOnChange: true)
                .AddJsonFile($"{appSettings}.{environmentName}.{json}", optional: true, reloadOnChange: true);

            return builder.Build();
        }

        private string GetConnectionStringFromAppSettings(IConfigurationRoot configuration, IFeatureToggle toggle)
        {
            string prefixedToggleConfigName = $"{ToggleConfigurationSettings.Prefix}{toggle.GetType().Name}";
            string appSettingsConnectionStringKey = $"{prefixedToggleConfigName}::ConnectionString";

            string connectionString = Configuration.Get<string>(appSettingsConnectionStringKey);

            bool isConnectionStringMissing = string.IsNullOrWhiteSpace(connectionString);

            if (isConnectionStringMissing)
            {
                throw new ToggleConfigurationError(CouldNotFindValueForMessage, appSettingsConnectionStringKey);
            }
        }

        private string GetCommandTextFromAppSettings(IFeatureToggle toggle)
        {
            var sqlStatementKey = $"{ToggleConfigurationSettings.Prefix}{toggle.GetType().Name}::SqlStatement";

            string sqlStatement = Configuration.Get<string>(sqlStatementKey);

            bool isSqlStatementIsMissing = string.IsNullOrWhiteSpace(sqlStatement);

            if (isSqlStatementIsMissing)
            {
                throw new ToggleConfigurationError(string.Format(TheAppSettingsKeyWasNotFound, sqlStatementKey));
            }

            return sqlStatement;
        }

#endif

        private string GetConnectionStringFromConfig(IFeatureToggle toggle)
        {
            var prefixedToggleConfigName = ToggleConfigurationSettings.Prefix + toggle.GetType().Name;
            var appSettingsConnectionStringKey = prefixedToggleConfigName + ".ConnectionString";
            var appSettingsConnectionStringNameKey = prefixedToggleConfigName + ".ConnectionStringName";

            var isConfiguredViaAppSettingsConnectionString = ConfigurationManager.AppSettings.AllKeys.Contains(appSettingsConnectionStringKey);
            var isConfiguredViaAppSettingsConnectionStringName = ConfigurationManager.AppSettings.AllKeys.Contains(appSettingsConnectionStringNameKey);
            var isConfiguredViaConnectionStrings = ConfigurationManager.ConnectionStrings[prefixedToggleConfigName] != null;

            ValidateConnectionStringSpecifiedOnlyOnce(isConfiguredViaAppSettingsConnectionString, isConfiguredViaAppSettingsConnectionStringName, isConfiguredViaConnectionStrings, prefixedToggleConfigName);
            ValidateConnectionStringNotMissing(isConfiguredViaAppSettingsConnectionString, isConfiguredViaAppSettingsConnectionStringName, isConfiguredViaConnectionStrings, appSettingsConnectionStringKey, appSettingsConnectionStringNameKey, prefixedToggleConfigName);


            if (isConfiguredViaAppSettingsConnectionString)
            {
                return GetAppSettingsConnectionString(appSettingsConnectionStringKey);
            }
            
            if (isConfiguredViaAppSettingsConnectionStringName)
            {
                return GetConnectionStringFromAppSettingsValueThatPointsToNamedConnectionString(appSettingsConnectionStringNameKey);
            }

            return GetConnectionStringDirectlyFromNamedConnectionStrings(prefixedToggleConfigName);
        }

        private static void ValidateConnectionStringNotMissing(bool isConfiguredViaAppSettingsConnectionString,
            bool isConfiguredViaAppSettingsConnectionStringName, bool isConfiguredViaConnectionStrings,
            string appSettingsConnectionStringKey, string appSettingsConnectionStringNameKey, string prefixedToggleConfigName)
        {
            var noConnectionStringConfigured = !isConfiguredViaAppSettingsConnectionString &&
                                               !isConfiguredViaAppSettingsConnectionStringName &&
                                               !isConfiguredViaConnectionStrings;

            if (noConnectionStringConfigured)
            {
                throw new ToggleConfigurationError(
                    string.Format(
                        "The connection string was not configured in <appSettings> with a key of '{0}' or '{1}' nor in <connectionStrings> with a name of '{2}'.",
                        appSettingsConnectionStringKey, appSettingsConnectionStringNameKey, prefixedToggleConfigName));
            }
        }

        private void ValidateConnectionStringSpecifiedOnlyOnce(bool isConfiguredViaAppSettingsConnectionString,
            bool isConfiguredViaAppSettingsConnectionStringName, bool isConfiguredViaConnectionStrings,
            string prefixedToggleConfigName)
        {
            var connectionStringConfiguredInMultiplePlaces = new[]
                                                             {
                                                                 isConfiguredViaAppSettingsConnectionString,
                                                                 isConfiguredViaAppSettingsConnectionStringName,
                                                                 isConfiguredViaConnectionStrings
                                                             }.Count(x => x == true) > 1;


            if (connectionStringConfiguredInMultiplePlaces)
            {
                throw new ToggleConfigurationError(
                    string.Format(
                        "The connection string for '{0}' is configured multiple times.",
                        prefixedToggleConfigName));
            }
        }


        private static string GetConnectionStringDirectlyFromNamedConnectionStrings(string prefixedToggleConfigName)
        {
            var configuredConnectionString = ConfigurationManager.ConnectionStrings[prefixedToggleConfigName].ConnectionString;

            if (string.IsNullOrWhiteSpace(configuredConnectionString))
            {
                throw new ToggleConfigurationError(string.Format(NamedConnectionStringsValueIsEmptyErrorMessage, prefixedToggleConfigName));
            }

            return configuredConnectionString;
        }

        private static string GetConnectionStringFromAppSettingsValueThatPointsToNamedConnectionString(string appSettingsConnectionStringNameKey)
        {
            var connectionStringName = ConfigurationManager.AppSettings[appSettingsConnectionStringNameKey];

            if (string.IsNullOrWhiteSpace(connectionStringName))
            {
                throw new ToggleConfigurationError(string.Format(AppSettingsKeyValuesIsEmptyErrorMessage,
                    appSettingsConnectionStringNameKey));
            }

            var connectionStringSettings = ConfigurationManager.ConnectionStrings[connectionStringName];

            if (connectionStringSettings == null)
            {
                throw new ToggleConfigurationError(string.Format("No entry named '{0}' exists in <connectionStrings>.",
                    connectionStringName));
            }

            var configuredConnectionString = connectionStringSettings.ConnectionString;

            if (string.IsNullOrWhiteSpace(configuredConnectionString))
            {
                throw new ToggleConfigurationError(string.Format(NamedConnectionStringsValueIsEmptyErrorMessage,
                    connectionStringName));
            }

            return configuredConnectionString;
        }

        private static string GetAppSettingsConnectionString(string appSettingsConnectionStringKey)
        {
            var configuredConnectionString = ConfigurationManager.AppSettings[appSettingsConnectionStringKey];

            if (string.IsNullOrWhiteSpace(configuredConnectionString))
            {
                throw new ToggleConfigurationError(string.Format(AppSettingsKeyValuesIsEmptyErrorMessage,
                    appSettingsConnectionStringKey));
            }
            return configuredConnectionString;
        }



        private string GetCommandTextFromAppConfig(IFeatureToggle toggle)
        {
            var sqlCommandKey = ToggleConfigurationSettings.Prefix + toggle.GetType().Name + ".SqlStatement";

            var sqlCommandIsMissing = !ConfigurationManager.AppSettings.AllKeys.Contains(sqlCommandKey);

            if (sqlCommandIsMissing)
            {
                throw new ToggleConfigurationError(string.Format(TheAppSettingsKeyWasNotFound,
                    sqlCommandKey));
            }

            var configuredSqlCommand = ConfigurationManager.AppSettings[sqlCommandKey];

            if (string.IsNullOrWhiteSpace(configuredSqlCommand))
            {
                throw new ToggleConfigurationError(string.Format(AppSettingsKeyValuesIsEmptyErrorMessage, sqlCommandKey));
            }

            return configuredSqlCommand;
        }

#endif

#if NETFULL || NETCORE

        private bool RunQuery(string connectionString, string sqlCommandText)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();

                using (var cmd = new SqlCommand(sqlCommandText, connection))
                {
                    return (bool)cmd.ExecuteScalar();
                }
            }
        }

#endif

    }
}


