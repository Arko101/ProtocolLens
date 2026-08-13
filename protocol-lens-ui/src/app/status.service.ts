import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';

interface StatusResponse {
  message: string;
}

@Injectable({
  providedIn: 'root',
})
export class StatusService {
  constructor(private http: HttpClient) {}

  getStatus() {
    return this.http.get<StatusResponse>('http://localhost:5000/api/status');
  }
}
