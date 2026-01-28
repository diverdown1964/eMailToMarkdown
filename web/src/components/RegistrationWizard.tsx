import { useState, useCallback } from 'react'
import { useMsal, useIsAuthenticated } from '@azure/msal-react'
import { InteractionStatus, AuthenticationResult } from '@azure/msal-browser'
import { loginRequest } from '../auth/msalConfig'
import { registerUser, RegistrationData } from '../api/authApi'

type Step = 'signin' | 'folder' | 'complete'

export function RegistrationWizard() {
  const { instance, inProgress, accounts } = useMsal()
  const isAuthenticated = useIsAuthenticated()

  const [currentStep, setCurrentStep] = useState<Step>('signin')
  const [rootFolder, setRootFolder] = useState('/EmailToMarkdown')
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [authResult, setAuthResult] = useState<AuthenticationResult | null>(null)
  const [copied, setCopied] = useState(false)

  const handleSignIn = useCallback(async () => {
    setIsLoading(true)
    setError(null)

    try {
      // Use popup for better UX
      const result = await instance.loginPopup({
        ...loginRequest,
        prompt: 'consent' // Ensure user sees consent screen
      })

      setAuthResult(result)
      setCurrentStep('folder')
    } catch (err) {
      console.error('Login failed:', err)
      setError('Sign in failed. Please try again.')
    } finally {
      setIsLoading(false)
    }
  }, [instance])

  const handleFolderSubmit = useCallback(async () => {
    if (!authResult || !accounts[0]) {
      setError('Authentication expired. Please sign in again.')
      setCurrentStep('signin')
      return
    }

    setIsLoading(true)
    setError(null)

    try {
      // Get a fresh token with the required scopes
      const tokenResponse = await instance.acquireTokenSilent({
        ...loginRequest,
        account: accounts[0]
      })

      const registrationData: RegistrationData = {
        provider: 'microsoft',
        email: accounts[0].username,
        accessToken: tokenResponse.accessToken,
        refreshToken: '', // MSAL doesn't expose refresh token to browser apps
        expiresIn: Math.floor((tokenResponse.expiresOn!.getTime() - Date.now()) / 1000),
        rootFolder: rootFolder.startsWith('/') ? rootFolder : `/${rootFolder}`
      }

      const result = await registerUser(registrationData)

      if (result.success) {
        setCurrentStep('complete')
      } else {
        setError(result.error || 'Registration failed. Please try again.')
      }
    } catch (err) {
      console.error('Registration failed:', err)
      setError('Registration failed. Please try again.')
    } finally {
      setIsLoading(false)
    }
  }, [authResult, accounts, instance, rootFolder])

  const handleCopyEmail = useCallback(() => {
    navigator.clipboard.writeText('convert@markdown.whites.site')
    setCopied(true)
    setTimeout(() => setCopied(false), 2000)
  }, [])

  const handleSignOut = useCallback(async () => {
    await instance.logoutPopup()
    setCurrentStep('signin')
    setAuthResult(null)
    setRootFolder('/EmailToMarkdown')
  }, [instance])

  // Determine which step is active/completed
  const getStepState = (step: Step): 'pending' | 'active' | 'completed' => {
    const steps: Step[] = ['signin', 'folder', 'complete']
    const currentIndex = steps.indexOf(currentStep)
    const stepIndex = steps.indexOf(step)

    if (stepIndex < currentIndex) return 'completed'
    if (stepIndex === currentIndex) return 'active'
    return 'pending'
  }

  if (inProgress !== InteractionStatus.None) {
    return (
      <div className="wizard">
        <div className="step-content">
          <div className="spinner" style={{ margin: '0 auto' }}></div>
          <p style={{ marginTop: '16px', color: '#666' }}>Processing...</p>
        </div>
      </div>
    )
  }

  return (
    <div className="wizard">
      {/* Step indicators */}
      <div className="step-indicator">
        <div className={`step-dot ${getStepState('signin')}`}></div>
        <div className={`step-dot ${getStepState('folder')}`}></div>
        <div className={`step-dot ${getStepState('complete')}`}></div>
      </div>

      {/* Step 1: Sign In */}
      {currentStep === 'signin' && (
        <div className="step-content">
          <h2 className="step-title">Sign In</h2>
          <p className="step-description">
            Sign in with your Microsoft account to get started.
            We support both work/school and personal accounts.
          </p>

          <button
            className="btn btn-primary"
            onClick={handleSignIn}
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

          {error && <div className="error-message">{error}</div>}
        </div>
      )}

      {/* Step 2: Select Folder */}
      {currentStep === 'folder' && (
        <div className="step-content">
          <h2 className="step-title">Choose Storage Folder</h2>
          <p className="step-description">
            Enter the OneDrive folder path where your converted emails will be saved.
            The folder will be created if it doesn't exist.
          </p>

          <div className="form-group">
            <label htmlFor="rootFolder">Folder Path</label>
            <input
              id="rootFolder"
              type="text"
              value={rootFolder}
              onChange={(e) => setRootFolder(e.target.value)}
              placeholder="/EmailToMarkdown"
            />
          </div>

          <button
            className="btn btn-primary"
            onClick={handleFolderSubmit}
            disabled={isLoading || !rootFolder.trim()}
          >
            {isLoading ? (
              <>
                <span className="spinner"></span>
                Saving...
              </>
            ) : (
              'Complete Setup'
            )}
          </button>

          {error && <div className="error-message">{error}</div>}
        </div>
      )}

      {/* Step 3: Complete */}
      {currentStep === 'complete' && (
        <div className="step-content">
          <div className="success-icon">
            <CheckIcon />
          </div>

          <h2 className="step-title">You're All Set!</h2>
          <p className="step-description">
            Your account is configured. Forward any email to the address below
            and it will be converted to Markdown and saved to your OneDrive.
          </p>

          <div className="email-box">
            <code>convert@markdown.whites.site</code>
            <button className="copy-btn" onClick={handleCopyEmail}>
              {copied ? 'Copied!' : 'Copy'}
            </button>
          </div>

          <p style={{ fontSize: '14px', color: '#666', marginBottom: '24px' }}>
            Your files will be saved to: <strong>{rootFolder}</strong>
          </p>

          <button className="btn btn-secondary" onClick={handleSignOut}>
            Sign Out
          </button>
        </div>
      )}
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

function CheckIcon() {
  return (
    <svg
      width="32"
      height="32"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="3"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <polyline points="20 6 9 17 4 12" />
    </svg>
  )
}
