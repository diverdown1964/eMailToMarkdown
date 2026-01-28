import { useIsAuthenticated, useMsal } from '@azure/msal-react'
import { RegistrationWizard } from './components/RegistrationWizard'
import './App.css'

function App() {
  const isAuthenticated = useIsAuthenticated()
  const { accounts } = useMsal()

  return (
    <div className="app">
      <div className="card">
        <h1>Email to Markdown</h1>
        <p className="subtitle">Convert your emails to Markdown files in OneDrive</p>

        <RegistrationWizard />

        {isAuthenticated && accounts[0] && (
          <p className="signed-in-as">
            Signed in as: {accounts[0].username}
          </p>
        )}
      </div>
    </div>
  )
}

export default App
