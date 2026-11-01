// Consistent base URL for all backend calls. The ASP.NET Core project's http launch
// profile (ProtocolLens.Api/Properties/launchSettings.json) binds to port 5015, which
// is what this must match (corrects the previous 5000/5015 mismatch).
export const API_BASE_URL = 'http://localhost:5015';
