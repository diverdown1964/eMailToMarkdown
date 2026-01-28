import { Configuration, LogLevel } from '@azure/msal-browser'

// Replace with your Azure AD app registration values
const clientId = import.meta.env.VITE_CLIENT_ID || 'YOUR_CLIENT_ID'
const redirectUri = import.meta.env.VITE_REDIRECT_URI || 'http://localhost:3000/auth/callback'

export const msalConfig: Configuration = {
  auth: {
    clientId,
    authority: 'https://login.microsoftonline.com/common', // Multi-tenant + personal accounts
    redirectUri,
    postLogoutRedirectUri: '/',
    navigateToLoginRequestUrl: true
  },
  cache: {
    cacheLocation: 'localStorage', // Persist across browser sessions
    storeAuthStateInCookie: false
  },
  system: {
    loggerOptions: {
      loggerCallback: (level, message, containsPii) => {
        if (containsPii) return
        switch (level) {
          case LogLevel.Error:
            console.error(message)
            return
          case LogLevel.Warning:
            console.warn(message)
            return
          case LogLevel.Info:
            console.info(message)
            return
          case LogLevel.Verbose:
            console.debug(message)
            return
        }
      }
    }
  }
}

// Basic login - just get user identity
export const loginRequest = {
  scopes: ['User.Read']
}

// OneDrive consent - request when user clicks "Connect to OneDrive"
export const oneDriveConsentRequest = {
  scopes: ['User.Read', 'Files.ReadWrite', 'offline_access']
}

// Future: Google Drive scopes
// export const googleDriveConsentRequest = { ... }

export const graphConfig = {
  graphMeEndpoint: 'https://graph.microsoft.com/v1.0/me'
}
