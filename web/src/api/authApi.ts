const API_BASE = import.meta.env.VITE_API_BASE || '/api'

export interface RegistrationData {
  provider: string
  email: string
  accessToken?: string
  refreshToken?: string
  expiresIn?: number
  rootFolder?: string
  folderId?: string
  driveId?: string
  linkedEmail?: string
  // Authorization code flow fields (preferred for refresh tokens)
  code?: string
  codeVerifier?: string
  redirectUri?: string
}

export interface ProviderInfo {
  provider: string
  rootFolder: string
  isActive: boolean
  consentGrantedAt?: string
  lastSuccessfulSync?: string
}

export interface LinkedIdentity {
  email: string
  provider: string
}

export interface ProvidersResponse {
  email: string
  providers: ProviderInfo[]
  linkedIdentities: LinkedIdentity[]
}

export interface RegistrationResponse {
  success: boolean
  email?: string
  rootFolder?: string
  error?: string
}

export async function registerUser(data: RegistrationData): Promise<RegistrationResponse> {
  const response = await fetch(`${API_BASE}/auth/register`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(data)
  })

  if (!response.ok) {
    const text = await response.text()
    console.error('Registration failed:', response.status, text)
    try {
      const errorData = JSON.parse(text)
      return { success: false, error: errorData.error || `HTTP ${response.status}` }
    } catch {
      return { success: false, error: `HTTP ${response.status}: ${text}` }
    }
  }

  const text = await response.text()
  if (!text) {
    return { success: false, error: 'Empty response from server' }
  }

  try {
    return JSON.parse(text)
  } catch (err) {
    console.error('Failed to parse response:', text)
    return { success: false, error: 'Invalid response from server' }
  }
}

export async function checkStatus(email: string): Promise<{
  isRegistered: boolean
  storageProvider?: string
  rootFolder?: string
  lastSuccessfulSync?: string
}> {
  const response = await fetch(`${API_BASE}/auth/status/${encodeURIComponent(email)}`)
  return response.json()
}

export async function getProviders(email: string): Promise<ProvidersResponse> {
  const response = await fetch(`${API_BASE}/auth/providers/${encodeURIComponent(email)}`)
  if (!response.ok) {
    throw new Error(`Failed to get providers: ${response.status}`)
  }
  return response.json()
}
