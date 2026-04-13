import { Injectable } from '@angular/core';
import { BehaviorSubject, interval, switchMap } from 'rxjs';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environment/env';
import { PollResponseDto, TrnItemDto, AckRequestDto } from '../models/poll';

@Injectable({ providedIn: 'root' })
export class PollService {
  private apiUrl = `${environment.baseUrl}/poll`;

  private currentBatchToken: string | null = null;
  private currentRows: TrnItemDto[] = [];

  private trnStream = new BehaviorSubject<TrnItemDto[]>([]);
  trn$ = this.trnStream.asObservable();

  constructor(private http: HttpClient) {}

  startPolling() {
    interval(8000)
      .pipe(
        switchMap(() =>
          this.http.get<PollResponseDto>(this.apiUrl, {
            withCredentials: true
          })
        )
      )
      .subscribe(res => this.handleResponse(res));
  }

  private handleResponse(res: PollResponseDto) {
    // CASE: Server says ACK first
    if (res.needAckFirst && res.batchToken) {
      this.currentBatchToken = res.batchToken;
      return;
    }

    // CASE: New data arrived
    if (res.hasData && res.rows && res.batchToken) {
      this.currentBatchToken = res.batchToken;
      this.currentRows = res.rows;
      this.trnStream.next(res.rows);
    }
  }

  // Component calls this AFTER device processing is done
  ackBatch() {
    if (!this.currentBatchToken || this.currentRows.length === 0) return;

    const ack: AckRequestDto = {
      batchToken: this.currentBatchToken,
      trnIDs: this.currentRows.map(x => x.trnID)
    };

    this.http
      .post(`${this.apiUrl}/ack`, ack, { withCredentials: true })
      .subscribe(() => {
        this.currentBatchToken = null;
        this.currentRows = [];
      });
  }
}