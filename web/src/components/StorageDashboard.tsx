import { useState, useCallback, useEffect, useRef } from 'react'
import { useMsal } from '@azure/msal-react'
import { InteractionStatus } from '@azure/msal-browser'
import { loginRequest } from '../auth/msalConfig'
import { registerUser, RegistrationData, getProviders, checkStatus } from '../api/authApi'

const GOOGLE_USER_KEY = 'emailtomarkdown_google_user'

// PKCE helper functions
function generateCodeVerifier(): string {
  const array = new Uint8Array(32)
  crypto.getRandomValues(array)
  return base64UrlEncode(array)
}

async function generateCodeChallenge(verifier: string): Promise<string> {
  const encoder = new TextEncoder()
  const data = encoder.encode(verifier)
  const hash = await crypto.subtle.digest('SHA-256', data)
  return base64UrlEncode(new Uint8Array(hash))
}

function base64UrlEncode(buffer: Uint8Array): string {
  let binary = ''
  for (let i = 0; i < buffer.length; i++) {
    binary += String.fromCharCode(buffer[i])
  }
  return btoa(binary)
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '')
}

interface GoogleUser {
  email: string
  name: string
}

interface ProviderStatus {
  isConnected: boolean
  isChecking: boolean
  rootFolder?: string
  lastSync?: string
}

const initialProviderStatus: ProviderStatus = {
  isConnected: false,
  isChecking: false
}

export function StorageDashboard() {
  const { instance, inProgress, accounts } = useMsal()
  const isMicrosoftAuthenticated = accounts.length > 0

  // Google user state - persisted to localStorage
  const [googleUser, setGoogleUser] = useState<GoogleUser | null>(() => {
    const stored = localStorage.getItem(GOOGLE_USER_KEY)
    return stored ? JSON.parse(stored) : null
  })

  // Storage provider statuses
  const [oneDriveStatus, setOneDriveStatus] = useState<ProviderStatus>(initialProviderStatus)
  const [googleDriveStatus, setGoogleDriveStatus] = useState<ProviderStatus>(initialProviderStatus)

  // UI state
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

  // Separate folder inputs for each provider
  const [oneDriveFolderInput, setOneDriveFolderInput] = useState('/EmailToMarkdown')
  const [googleDriveFolderInput, setGoogleDriveFolderInput] = useState('/EmailToMarkdown')

  // Edit mode states
  const [editingOneDriveFolder, setEditingOneDriveFolder] = useState(false)
  const [editingGoogleDriveFolder, setEditingGoogleDriveFolder] = useState(false)
  const [showOneDriveFolderInput, setShowOneDriveFolderInput] = useState(false)
  const [showGoogleDriveFolderInput, setShowGoogleDriveFolderInput] = useState(false)

  // Prevent duplicate status checks
  const hasCheckedOneDrive = useRef(false)
  const hasCheckedGoogleDrive = useRef(false)

  // Persist Google user to localStorage
  useEffect(() => {
    if (googleUser) {
      localStorage.setItem(GOOGLE_USER_KEY, JSON.stringify(googleUser))
    } else {
      localStorage.removeItem(GOOGLE_USER_KEY)
    }
  }, [googleUser])

  // Handle OAuth callback for authorization code flow
  useEffect(() => {
    const handleAuthCallback = async () => {
      const urlParams = new URLSearchParams(window.location.search)
      const code = urlParams.get('code')
      const state = urlParams.get('state')
      const errorParam = urlParams.get('error')
      const errorDescription = urlParams.get('error_description')

      // Check if this is an OAuth callback
      if (!code && !errorParam) return

      // Clean up URL
      window.history.replaceState({}, document.title, window.location.pathname)

      if (errorParam) {
        setError(`Authorization failed: ${errorDescription || errorParam}`)
        return
      }

      if (code && state === 'onedrive_connect') {
        setIsLoading(true)
        try {
          // Retrieve stored values
          const codeVerifier = sessionStorage.getItem('pkce_code_verifier')
          const folderPath = sessionStorage.getItem('onedrive_folder') || '/EmailToMarkdown'
          const linkedEmail = sessionStorage.getItem('linked_email') || ''

          // Clean up session storage
          sessionStorage.removeItem('pkce_code_verifier')
          sessionStorage.removeItem('onedrive_folder')
          sessionStorage.removeItem('linked_email')

          if (!codeVerifier) {
            setError('Missing code verifier. Please try connecting again.')
            setIsLoading(false)
            return
          }

          // Wait for MSAL to be ready
          if (inProgress !== InteractionStatus.None || accounts.length === 0) {
            // Store the auth data temporarily and retry after MSAL is ready
            sessionStorage.setItem('pending_auth_code', code)
            sessionStorage.setItem('pending_code_verifier', codeVerifier)
            sessionStorage.setItem('pending_folder_path', folderPath)
            sessionStorage.setItem('pending_linked_email', linkedEmail)
            sessionStorage.setItem('pending_provider', 'microsoft')
            console.log('MSAL not ready, storing pending auth data')
            setIsLoading(false)
            return
          }

          const redirectUri = window.location.origin + '/auth/callback'
          const registrationData: RegistrationData = {
            provider: 'microsoft',
            email: accounts[0].username,
            code: code,
            codeVerifier: codeVerifier,
            redirectUri: redirectUri,
            rootFolder: folderPath.startsWith('/') ? folderPath : `/${folderPath}`,
            linkedEmail: linkedEmail || undefined
          }

          console.log('Registering with authorization code:', { email: registrationData.email, hasCode: !!code })
          const result = await registerUser(registrationData)

          if (result.success) {
            setOneDriveStatus({
              isConnected: true,
              isChecking: false,
              rootFolder: result.rootFolder || folderPath
            })
            setShowOneDriveFolderInput(false)
            setEditingOneDriveFolder(false)
            // Reset flags to refresh provider list
            hasCheckedOneDrive.current = false
            hasCheckedGoogleDrive.current = false
          } else {
            setError(result.error || 'Failed to complete OneDrive connection')
          }
        } catch (err: any) {
          console.error('Auth callback failed:', err)
          setError(err?.message || 'Failed to complete authorization')
        } finally {
          setIsLoading(false)
        }
      }

      // Handle Google Drive auth callback
      if (code && state === 'googledrive_connect') {
        setIsLoading(true)
        try {
          // Retrieve stored values
          const codeVerifier = sessionStorage.getItem('pkce_code_verifier')
          const folderPath = sessionStorage.getItem('googledrive_folder') || '/EmailToMarkdown'
          const googleEmail = sessionStorage.getItem('google_user_email') || ''
          const linkedEmail = sessionStorage.getItem('linked_email') || ''

          // Clean up session storage
          sessionStorage.removeItem('pkce_code_verifier')
          sessionStorage.removeItem('googledrive_folder')
          sessionStorage.removeItem('google_user_email')
          sessionStorage.removeItem('linked_email')

          if (!codeVerifier || !googleEmail) {
            setError('Missing authorization data. Please try connecting again.')
            setIsLoading(false)
            return
          }

          const redirectUri = window.location.origin + '/auth/callback'
          const registrationData: RegistrationData = {
            provider: 'google',
            email: googleEmail,
            code: code,
            codeVerifier: codeVerifier,
            redirectUri: redirectUri,
            rootFolder: folderPath.startsWith('/') ? folderPath : `/${folderPath}`,
            linkedEmail: linkedEmail || undefined
          }

          console.log('Registering Google Drive with authorization code:', { email: googleEmail, hasCode: !!code })
          const result = await registerUser(registrationData)

          if (result.success) {
            setGoogleDriveStatus({
              isConnected: true,
              isChecking: false,
              rootFolder: result.rootFolder || folderPath
            })
            setShowGoogleDriveFolderInput(false)
            setEditingGoogleDriveFolder(false)
            // Reset flags to refresh provider list
            hasCheckedOneDrive.current = false
            hasCheckedGoogleDrive.current = false
          } else {
            setError(result.error || 'Failed to complete Google Drive connection')
          }
        } catch (err: any) {
          console.error('Google auth callback failed:', err)
          setError(err?.message || 'Failed to complete authorization')
        } finally {
          setIsLoading(false)
        }
      }
    }

    handleAuthCallback()
  }, [accounts, inProgress])

  // Handle pending auth when MSAL becomes ready
  useEffect(() => {
    const handlePendingAuth = async () => {
      if (inProgress !== InteractionStatus.None || accounts.length === 0) return

      const pendingCode = sessionStorage.getItem('pending_auth_code')
      if (!pendingCode) return

      const codeVerifier = sessionStorage.getItem('pending_code_verifier')
      const folderPath = sessionStorage.getItem('pending_folder_path') || '/EmailToMarkdown'
      const linkedEmail = sessionStorage.getItem('pending_linked_email') || ''

      // Clear pending data
      sessionStorage.removeItem('pending_auth_code')
      sessionStorage.removeItem('pending_code_verifier')
      sessionStorage.removeItem('pending_folder_path')
      sessionStorage.removeItem('pending_linked_email')

      if (!codeVerifier) return

      setIsLoading(true)
      try {
        const redirectUri = window.location.origin + '/auth/callback'
        const registrationData: RegistrationData = {
          provider: 'microsoft',
          email: accounts[0].username,
          code: pendingCode,
          codeVerifier: codeVerifier,
          redirectUri: redirectUri,
          rootFolder: folderPath.startsWith('/') ? folderPath : `/${folderPath}`,
          linkedEmail: linkedEmail || undefined
        }

        const result = await registerUser(registrationData)

        if (result.success) {
          setOneDriveStatus({
            isConnected: true,
            isChecking: false,
            rootFolder: result.rootFolder || folderPath
          })
          hasCheckedOneDrive.current = false
          hasCheckedGoogleDrive.current = false
        } else {
          setError(result.error || 'Failed to complete OneDrive connection')
        }
      } catch (err: any) {
        console.error('Pending auth failed:', err)
        setError(err?.message || 'Failed to complete authorization')
      } finally {
        setIsLoading(false)
      }
    }

    handlePendingAuth()
  }, [accounts, inProgress])

  // Check all provider statuses when any identity is authenticated
  // This uses the new getProviders endpoint which returns all connected providers
  // across linked identities
  useEffect(() => {
    const checkAllProviders = async () => {
      // Need at least one identity to check
      const hasIdentity = isMicrosoftAuthenticated || googleUser
      if (!hasIdentity || inProgress !== InteractionStatus.None) {
        return
      }

      // Only check once per session (until login/logout changes)
      if (hasCheckedOneDrive.current && hasCheckedGoogleDrive.current) {
        return
      }

      hasCheckedOneDrive.current = true
      hasCheckedGoogleDrive.current = true

      setOneDriveStatus(prev => ({ ...prev, isChecking: true }))
      setGoogleDriveStatus(prev => ({ ...prev, isChecking: true }))

      try {
        // Use whichever email we have - the backend will return all linked providers
        const email = isMicrosoftAuthenticated ? accounts[0].username : googleUser!.email
        const providersResponse = await getProviders(email)
        console.log('Providers response:', providersResponse)

        // Find Microsoft/OneDrive connection
        const microsoftProvider = providersResponse.providers.find(p => p.provider === 'microsoft')
        if (microsoftProvider && microsoftProvider.isActive) {
          setOneDriveStatus({
            isConnected: true,
            isChecking: false,
            rootFolder: microsoftProvider.rootFolder,
            lastSync: microsoftProvider.lastSuccessfulSync
          })
          if (microsoftProvider.rootFolder) {
            setOneDriveFolderInput(microsoftProvider.rootFolder)
          }
        } else {
          setOneDriveStatus({ isConnected: false, isChecking: false })
        }

        // Find Google Drive connection
        const googleProvider = providersResponse.providers.find(p => p.provider === 'google')
        if (googleProvider && googleProvider.isActive) {
          setGoogleDriveStatus({
            isConnected: true,
            isChecking: false,
            rootFolder: googleProvider.rootFolder,
            lastSync: googleProvider.lastSuccessfulSync
          })
          if (googleProvider.rootFolder) {
            setGoogleDriveFolderInput(googleProvider.rootFolder)
          }
        } else {
          setGoogleDriveStatus({ isConnected: false, isChecking: false })
        }
      } catch (err) {
        // Fall back to legacy checkStatus if new endpoint not available (404)
        console.warn('getProviders failed, falling back to checkStatus:', err)

        // Check each provider separately using the old endpoint
        if (isMicrosoftAuthenticated) {
          try {
            const status = await checkStatus(accounts[0].username)
            if (status.isRegistered && status.storageProvider === 'microsoft') {
              setOneDriveStatus({
                isConnected: true,
                isChecking: false,
                rootFolder: status.rootFolder,
                lastSync: status.lastSuccessfulSync
              })
              if (status.rootFolder) {
                setOneDriveFolderInput(status.rootFolder)
              }
            } else {
              setOneDriveStatus({ isConnected: false, isChecking: false })
            }
          } catch {
            setOneDriveStatus({ isConnected: false, isChecking: false })
          }
        } else {
          setOneDriveStatus({ isConnected: false, isChecking: false })
        }

        if (googleUser) {
          try {
            const status = await checkStatus(googleUser.email)
            if (status.isRegistered && status.storageProvider === 'google') {
              setGoogleDriveStatus({
                isConnected: true,
                isChecking: false,
                rootFolder: status.rootFolder,
                lastSync: status.lastSuccessfulSync
              })
              if (status.rootFolder) {
                setGoogleDriveFolderInput(status.rootFolder)
              }
            } else {
              setGoogleDriveStatus({ isConnected: false, isChecking: false })
            }
          } catch {
            setGoogleDriveStatus({ isConnected: false, isChecking: false })
          }
        } else {
          setGoogleDriveStatus({ isConnected: false, isChecking: false })
        }
      }
    }

    checkAllProviders()
  }, [isMicrosoftAuthenticated, accounts, googleUser, inProgress])

  // Microsoft Sign-In
  const handleMicrosoftSignIn = useCallback(async () => {
    setIsLoading(true)
    setError(null)
    try {
      await instance.loginPopup(loginRequest)
      // Reset both flags so we re-fetch all providers with the new identity
      hasCheckedOneDrive.current = false
      hasCheckedGoogleDrive.current = false
    } catch (err: any) {
      console.error('Microsoft login failed:', err)
      if (!err.message?.includes('user_cancelled')) {
        setError(err?.message || 'Sign in failed. Please try again.')
      }
    } finally {
      setIsLoading(false)
    }
  }, [instance])

  // Google Sign-In callback handler
  const handleGoogleCredentialResponse = useCallback((response: any) => {
    try {
      const payload = JSON.parse(atob(response.credential.split('.')[1]))
      const user: GoogleUser = {
        email: payload.email,
        name: payload.name
      }
      setGoogleUser(user)
      // Reset both flags so we re-fetch all providers with the new identity
      hasCheckedOneDrive.current = false
      hasCheckedGoogleDrive.current = false
    } catch (err) {
      console.error('Failed to parse Google credential:', err)
      setError('Failed to sign in with Google')
    }
  }, [])

  // Google Sign-In
  const handleGoogleSignIn = useCallback(() => {
    setIsLoading(true)
    setError(null)
    try {
      const google = (window as any).google
      if (!google?.accounts?.id) {
        setError('Google Sign-In not loaded. Please refresh the page.')
        setIsLoading(false)
        return
      }
      google.accounts.id.prompt()
    } catch (err: any) {
      console.error('Google login failed:', err)
      setError(err?.message || 'Google sign in failed. Please try again.')
    } finally {
      setIsLoading(false)
    }
  }, [])

  // Initialize Google Sign-In
  useEffect(() => {
    const initGoogleSignIn = () => {
      const google = (window as any).google
      if (google?.accounts?.id) {
        google.accounts.id.initialize({
          client_id: import.meta.env.VITE_GOOGLE_CLIENT_ID || '',
          callback: handleGoogleCredentialResponse,
          auto_select: false
        })
      }
    }

    if (!(window as any).google?.accounts?.id) {
      const script = document.createElement('script')
      script.src = 'https://accounts.google.com/gsi/client'
      script.async = true
      script.defer = true
      script.onload = initGoogleSignIn
      document.body.appendChild(script)
    } else {
      initGoogleSignIn()
    }
  }, [handleGoogleCredentialResponse])

  // Sign Out Microsoft
  const handleMicrosoftSignOut = useCallback(async () => {
    try {
      await instance.logoutPopup()
      setOneDriveStatus(initialProviderStatus)
      setGoogleDriveStatus(initialProviderStatus)
      // Reset both flags so that logging in with Google will re-fetch all providers
      hasCheckedOneDrive.current = false
      hasCheckedGoogleDrive.current = false
    } catch (err) {
      console.error('Microsoft logout failed:', err)
    }
  }, [instance])

  // Sign Out Google
  const handleGoogleSignOut = useCallback(() => {
    const google = (window as any).google
    google?.accounts?.id?.disableAutoSelect()
    setGoogleUser(null)
    setOneDriveStatus(initialProviderStatus)
    setGoogleDriveStatus(initialProviderStatus)
    // Reset both flags so that logging in with Microsoft will re-fetch all providers
    hasCheckedOneDrive.current = false
    hasCheckedGoogleDrive.current = false
  }, [])

  // Connect/Update OneDrive - Using Authorization Code flow for refresh tokens
  const handleSaveOneDrive = useCallback(async () => {
    if (!accounts[0]) return

    setIsLoading(true)
    setError(null)
    try {
      // Generate PKCE code verifier and challenge
      const codeVerifier = generateCodeVerifier()
      const codeChallenge = await generateCodeChallenge(codeVerifier)
      
      // Store code verifier in sessionStorage for the callback
      sessionStorage.setItem('pkce_code_verifier', codeVerifier)
      sessionStorage.setItem('onedrive_folder', oneDriveFolderInput)
      sessionStorage.setItem('linked_email', googleUser?.email || '')

      // Build authorization URL with PKCE
      const redirectUri = window.location.origin + '/auth/callback'
      const scopes = ['openid', 'profile', 'User.Read', 'Files.ReadWrite', 'offline_access']
      
      const authUrl = new URL('https://login.microsoftonline.com/common/oauth2/v2.0/authorize')
      authUrl.searchParams.set('client_id', import.meta.env.VITE_CLIENT_ID)
      authUrl.searchParams.set('response_type', 'code')
      authUrl.searchParams.set('redirect_uri', redirectUri)
      authUrl.searchParams.set('scope', scopes.join(' '))
      authUrl.searchParams.set('response_mode', 'query')
      authUrl.searchParams.set('code_challenge', codeChallenge)
      authUrl.searchParams.set('code_challenge_method', 'S256')
      authUrl.searchParams.set('login_hint', accounts[0].username)
      authUrl.searchParams.set('prompt', 'consent') // Force consent to get refresh token
      authUrl.searchParams.set('state', 'onedrive_connect')

      // Redirect to Microsoft authorization
      window.location.href = authUrl.toString()
    } catch (err: any) {
      console.error('OneDrive connect failed:', err)
      setError(err?.message || 'Failed to connect to OneDrive')
      setIsLoading(false)
    }
  }, [accounts, oneDriveFolderInput, googleUser])

  const handleDisconnectOneDrive = useCallback(() => {
    setOneDriveStatus(initialProviderStatus)
    setShowOneDriveFolderInput(false)
    setEditingOneDriveFolder(false)
    hasCheckedOneDrive.current = false
  }, [])

  // Connect/Update Google Drive - Using Authorization Code flow for refresh tokens
  const handleSaveGoogleDrive = useCallback(async () => {
    if (!googleUser) return

    setIsLoading(true)
    setError(null)
    try {
      // Generate PKCE code verifier and challenge
      const codeVerifier = generateCodeVerifier()
      const codeChallenge = await generateCodeChallenge(codeVerifier)
      
      // Store code verifier in sessionStorage for the callback
      sessionStorage.setItem('pkce_code_verifier', codeVerifier)
      sessionStorage.setItem('googledrive_folder', googleDriveFolderInput)
      sessionStorage.setItem('google_user_email', googleUser.email)
      sessionStorage.setItem('linked_email', isMicrosoftAuthenticated ? accounts[0]?.username || '' : '')

      // Build authorization URL with PKCE
      const redirectUri = window.location.origin + '/auth/callback'
      const scopes = [
        'openid',
        'profile',
        'email',
        'https://www.googleapis.com/auth/drive.file'
      ]
      
      const authUrl = new URL('https://accounts.google.com/o/oauth2/v2/auth')
      authUrl.searchParams.set('client_id', import.meta.env.VITE_GOOGLE_CLIENT_ID)
      authUrl.searchParams.set('response_type', 'code')
      authUrl.searchParams.set('redirect_uri', redirectUri)
      authUrl.searchParams.set('scope', scopes.join(' '))
      authUrl.searchParams.set('code_challenge', codeChallenge)
      authUrl.searchParams.set('code_challenge_method', 'S256')
      authUrl.searchParams.set('access_type', 'offline') // Request refresh token
      authUrl.searchParams.set('prompt', 'consent') // Force consent to get refresh token
      authUrl.searchParams.set('login_hint', googleUser.email)
      authUrl.searchParams.set('state', 'googledrive_connect')

      // Redirect to Google authorization
      window.location.href = authUrl.toString()
    } catch (err: any) {
      console.error('Google Drive connect failed:', err)
      setError(err?.message || 'Failed to connect to Google Drive')
      setIsLoading(false)
    }
  }, [googleUser, googleDriveFolderInput, isMicrosoftAuthenticated, accounts])

  const handleDisconnectGoogleDrive = useCallback(() => {
    setGoogleDriveStatus(initialProviderStatus)
    setShowGoogleDriveFolderInput(false)
    setEditingGoogleDriveFolder(false)
    hasCheckedGoogleDrive.current = false
  }, [])

  const handleCopyEmail = useCallback(() => {
    navigator.clipboard.writeText('convert@markdown.whites.site')
    setCopied(true)
    setTimeout(() => setCopied(false), 2000)
  }, [])

  // Edit handlers
  const handleEditOneDriveFolder = useCallback(() => {
    setEditingOneDriveFolder(true)
  }, [])

  const handleCancelOneDriveEdit = useCallback(() => {
    setEditingOneDriveFolder(false)
    setOneDriveFolderInput(oneDriveStatus.rootFolder || '/EmailToMarkdown')
  }, [oneDriveStatus.rootFolder])

  const handleEditGoogleDriveFolder = useCallback(() => {
    setEditingGoogleDriveFolder(true)
  }, [])

  const handleCancelGoogleDriveEdit = useCallback(() => {
    setEditingGoogleDriveFolder(false)
    setGoogleDriveFolderInput(googleDriveStatus.rootFolder || '/EmailToMarkdown')
  }, [googleDriveStatus.rootFolder])

  const isAnyAuthenticated = isMicrosoftAuthenticated || googleUser !== null
  const anyStorageConnected = oneDriveStatus.isConnected || googleDriveStatus.isConnected

  if (inProgress !== InteractionStatus.None) {
    return (
      <div className="dashboard">
        <div className="loading-state">
          <div className="spinner"></div>
          <p>Processing...</p>
        </div>
      </div>
    )
  }

  // Not signed in at all - show sign in options
  if (!isAnyAuthenticated) {
    return (
      <div className="dashboard">
        <div className="sign-in-card">
          <h2>Welcome to Email to Markdown</h2>
          <p>Sign in to get started.</p>

          <div className="sign-in-buttons">
            <button
              className="btn btn-microsoft"
              onClick={handleMicrosoftSignIn}
              disabled={isLoading}
            >
              {isLoading ? (
                <>
                  <span className="spinner"></span>
                  Signing in...
                </>
              ) : (
                <>
                  <MicrosoftLogo />
                  Sign in with Microsoft
                </>
              )}
            </button>

            <button
              className="btn btn-google"
              onClick={handleGoogleSignIn}
              disabled={isLoading}
            >
              <GoogleLogo />
              Sign in with Google
            </button>
          </div>

          {error && <div className="error-message">{error}</div>}
        </div>
      </div>
    )
  }

  // Signed in - show dashboard
  return (
    <div className="dashboard">
      {/* User accounts header */}
      <div className="user-header">
        {isMicrosoftAuthenticated && (
          <div className="user-account">
            <MicrosoftLogo />
            <span className="user-email">{accounts[0].username}</span>
            <button className="btn btn-link" onClick={handleMicrosoftSignOut}>Sign out</button>
          </div>
        )}
        {googleUser && (
          <div className="user-account">
            <GoogleLogo />
            <span className="user-email">{googleUser.email}</span>
            <button className="btn btn-link" onClick={handleGoogleSignOut}>Sign out</button>
          </div>
        )}
      </div>

      {anyStorageConnected && (
        <div className="instructions-card">
          <h3>How to use</h3>
          <p>Forward any email to this address and it will be converted to Markdown:</p>
          <div className="email-box">
            <code>convert@markdown.whites.site</code>
            <button className="copy-btn" onClick={handleCopyEmail}>
              {copied ? 'Copied!' : 'Copy'}
            </button>
          </div>
        </div>
      )}

      <h3 className="section-title">Storage Providers</h3>

      {error && <div className="error-message">{error}</div>}

      {/* OneDrive Card */}
      <div className={`provider-card ${oneDriveStatus.isConnected ? 'connected' : ''} ${!isMicrosoftAuthenticated ? 'disabled' : ''}`}>
        <div className="provider-header">
          <OneDriveLogo />
          <span className="provider-name">OneDrive</span>
          {oneDriveStatus.isConnected && <span className="status-badge">Connected</span>}
          {!isMicrosoftAuthenticated && <span className="requires-badge">Requires Microsoft login</span>}
        </div>

        <div className="provider-body">
          {!isMicrosoftAuthenticated ? (
            <>
              <p>Sign in with Microsoft to connect OneDrive.</p>
              <button className="btn btn-primary btn-sm" onClick={handleMicrosoftSignIn}>
                Sign in with Microsoft
              </button>
            </>
          ) : oneDriveStatus.isChecking ? (
            <>
              <span className="spinner spinner-small"></span> Checking status...
            </>
          ) : oneDriveStatus.isConnected && !editingOneDriveFolder ? (
            <>
              <div className="folder-display">
                <p><strong>Folder:</strong> {oneDriveStatus.rootFolder}</p>
                <button className="btn btn-link btn-edit" onClick={handleEditOneDriveFolder}>Edit</button>
              </div>
              {oneDriveStatus.lastSync && (
                <p className="last-sync">Last sync: {new Date(oneDriveStatus.lastSync).toLocaleString()}</p>
              )}
              <button className="btn btn-secondary btn-sm" onClick={handleDisconnectOneDrive}>
                Disconnect
              </button>
            </>
          ) : (showOneDriveFolderInput || editingOneDriveFolder) ? (
            <>
              <div className="form-group">
                <label htmlFor="oneDriveFolderInput">Folder path:</label>
                <input
                  id="oneDriveFolderInput"
                  type="text"
                  value={oneDriveFolderInput}
                  onChange={(e) => setOneDriveFolderInput(e.target.value)}
                  placeholder="/EmailToMarkdown"
                />
              </div>
              <div className="button-row">
                <button
                  className="btn btn-primary btn-sm"
                  onClick={handleSaveOneDrive}
                  disabled={isLoading || !oneDriveFolderInput.trim()}
                >
                  {isLoading ? (
                    <>
                      <span className="spinner spinner-small"></span>
                      Saving...
                    </>
                  ) : 'Save'}
                </button>
                <button
                  className="btn btn-secondary btn-sm"
                  onClick={editingOneDriveFolder ? handleCancelOneDriveEdit : () => setShowOneDriveFolderInput(false)}
                  disabled={isLoading}
                >
                  Cancel
                </button>
              </div>
            </>
          ) : (
            <>
              <p>Store converted emails in your OneDrive.</p>
              <button className="btn btn-primary btn-sm" onClick={() => setShowOneDriveFolderInput(true)}>
                Connect to OneDrive
              </button>
            </>
          )}
        </div>
      </div>

      {/* Google Drive Card */}
      <div className={`provider-card ${googleDriveStatus.isConnected ? 'connected' : ''} ${!googleUser ? 'disabled' : ''}`}>
        <div className="provider-header">
          <GoogleDriveLogo />
          <span className="provider-name">Google Drive</span>
          {googleDriveStatus.isConnected && <span className="status-badge">Connected</span>}
          {!googleUser && <span className="requires-badge">Requires Google login</span>}
        </div>

        <div className="provider-body">
          {!googleUser ? (
            <>
              <p>Sign in with Google to connect Google Drive.</p>
              <button className="btn btn-primary btn-sm" onClick={handleGoogleSignIn}>
                Sign in with Google
              </button>
            </>
          ) : googleDriveStatus.isChecking ? (
            <>
              <span className="spinner spinner-small"></span> Checking status...
            </>
          ) : googleDriveStatus.isConnected && !editingGoogleDriveFolder ? (
            <>
              <div className="folder-display">
                <p><strong>Folder:</strong> {googleDriveStatus.rootFolder}</p>
                <button className="btn btn-link btn-edit" onClick={handleEditGoogleDriveFolder}>Edit</button>
              </div>
              {googleDriveStatus.lastSync && (
                <p className="last-sync">Last sync: {new Date(googleDriveStatus.lastSync).toLocaleString()}</p>
              )}
              <button className="btn btn-secondary btn-sm" onClick={handleDisconnectGoogleDrive}>
                Disconnect
              </button>
            </>
          ) : (showGoogleDriveFolderInput || editingGoogleDriveFolder) ? (
            <>
              <div className="form-group">
                <label htmlFor="googleDriveFolderInput">Folder path:</label>
                <input
                  id="googleDriveFolderInput"
                  type="text"
                  value={googleDriveFolderInput}
                  onChange={(e) => setGoogleDriveFolderInput(e.target.value)}
                  placeholder="/EmailToMarkdown"
                />
              </div>
              <div className="button-row">
                <button
                  className="btn btn-primary btn-sm"
                  onClick={handleSaveGoogleDrive}
                  disabled={isLoading || !googleDriveFolderInput.trim()}
                >
                  {isLoading ? (
                    <>
                      <span className="spinner spinner-small"></span>
                      Saving...
                    </>
                  ) : 'Save'}
                </button>
                <button
                  className="btn btn-secondary btn-sm"
                  onClick={editingGoogleDriveFolder ? handleCancelGoogleDriveEdit : () => setShowGoogleDriveFolderInput(false)}
                  disabled={isLoading}
                >
                  Cancel
                </button>
              </div>
            </>
          ) : (
            <>
              <p>Store converted emails in your Google Drive.</p>
              <button className="btn btn-primary btn-sm" onClick={() => setShowGoogleDriveFolderInput(true)}>
                Connect to Google Drive
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  )
}

function MicrosoftLogo() {
  return (
    <svg width="20" height="20" viewBox="0 0 21 21" fill="none">
      <rect x="1" y="1" width="9" height="9" fill="#F25022" />
      <rect x="11" y="1" width="9" height="9" fill="#7FBA00" />
      <rect x="1" y="11" width="9" height="9" fill="#00A4EF" />
      <rect x="11" y="11" width="9" height="9" fill="#FFB900" />
    </svg>
  )
}

function GoogleLogo() {
  return (
    <svg width="20" height="20" viewBox="0 0 24 24">
      <path d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92c-.26 1.37-1.04 2.53-2.21 3.31v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.09z" fill="#4285F4"/>
      <path d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z" fill="#34A853"/>
      <path d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.07H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.93l2.85-2.22.81-.62z" fill="#FBBC05"/>
      <path d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.07l3.66 2.84c.87-2.6 3.3-4.53 6.16-4.53z" fill="#EA4335"/>
    </svg>
  )
}

function OneDriveLogo() {
  return (
    <svg width="24" height="24" viewBox="0 0 24 24" fill="none">
      <path d="M19.35 10.04C18.67 6.59 15.64 4 12 4C9.11 4 6.6 5.64 5.35 8.04C2.34 8.36 0 10.91 0 14C0 17.31 2.69 20 6 20H19C21.76 20 24 17.76 24 15C24 12.36 21.95 10.22 19.35 10.04Z" fill="#0078D4"/>
    </svg>
  )
}

function GoogleDriveLogo() {
  return (
    <svg width="24" height="24" viewBox="0 0 24 24" fill="none">
      <path d="M8.5 2L2 14H7.5L14 2H8.5Z" fill="#4285F4"/>
      <path d="M14 2L7.5 14L10.75 20H22L14 2Z" fill="#FBBC05"/>
      <path d="M2 14L5.25 20H16.75L13.5 14H2Z" fill="#34A853"/>
    </svg>
  )
}
