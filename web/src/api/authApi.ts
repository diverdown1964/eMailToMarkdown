const API_BASE = import.meta.env.VITE_API_BASE || '/api'

export interface RegistrationData {
  provider: string
  email: string
  accessToken: string
  refreshToken: string
  expiresIn: number
  rootFolder?: string
  folderId?: string
  driveId?: string
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

  return response.json()
}

export async function checkStatus(email: string): Promise<{
  isRegistered: boolean
  storageProvider?: string
  rootFolder?: string
}> {
  const response = await fetch(`${API_BASE}/auth/status/${encodeURIComponent(email)}`)
  return response.json()
}
