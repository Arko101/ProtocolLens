import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from './api-config';
import { AnalysisResponse } from './analysis-response.model';

@Injectable({
  providedIn: 'root',
})
export class PcapUploadService {
  constructor(private http: HttpClient) {}

  onUploadPcapFile(formData: FormData): Observable<AnalysisResponse> {
    return this.http.post<AnalysisResponse>(`${API_BASE_URL}/api/pcap`, formData);
  }
}
