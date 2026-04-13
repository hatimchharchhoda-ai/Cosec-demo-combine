import { Injectable } from '@angular/core';
import { AckRequestDto, PollRequestDto, PollResponseDto, TrnItemDto } from '../models/poll';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject } from 'rxjs/internal/BehaviorSubject';
import { interval } from 'rxjs/internal/observable/interval';
import { switchMap } from 'rxjs/internal/operators/switchMap';
import { environment } from '../../environment/env';

@Injectable({
  providedIn: 'root'
})
export class PollService {
  private apiUrl = `${environment.baseUrl}/poll`;

  private lastBatchToken: string | null = null;

  private trnStream = new BehaviorSubject<TrnItemDto[]>([]);
  trn$ = this.trnStream.asObservable();

  constructor(private http: HttpClient) {}

  startPolling() {
    interval(8000)
      .pipe(
        switchMap(() => {
          const body: PollRequestDto = {
            lastBatchToken: this.lastBatchToken
          };

          // POST, not GET
          return this.http.post<PollResponseDto>(
            this.apiUrl,
            body,
            { withCredentials: true }
          );
        })
      )
      .subscribe(res => this.handlePollResponse(res));
  }

  private handlePollResponse(res: PollResponseDto) {
    if (res.pendingAckRequired && res.pendingBatchToken) {
      this.lastBatchToken = res.pendingBatchToken;
      return;
    }

    if (res.hasData && res.items && res.batchToken) {
      this.lastBatchToken = res.batchToken;

      this.trnStream.next(res.items);

      const ack: AckRequestDto = {
        batchToken: res.batchToken,
        ackedTrnIDs: res.items.map(x => x.trnID)
      };

      this.http.post(`${this.apiUrl}/ack`, ack, {
        withCredentials: true
      }).subscribe();
    }
  }
}
