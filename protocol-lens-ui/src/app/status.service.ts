import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { API_BASE_URL } from './api-config';

interface StatusResponse {
  message: string;
}

@Injectable({
  providedIn: 'root',
})
export class StatusService {
  constructor(private http: HttpClient) {}

  getStatus() {
    return this.http.get<StatusResponse>(`${API_BASE_URL}/api/status`);
  }
}
