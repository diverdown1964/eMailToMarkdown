import { StorageDashboard } from './components/StorageDashboard'
import './App.css'

function App() {
  return (
    <div className="app">
      <div className="card">
        <h1>Email to Markdown</h1>
        <p className="subtitle">Convert your emails to Markdown files in your cloud storage</p>

        <StorageDashboard />
      </div>
    </div>
  )
}

export default App
