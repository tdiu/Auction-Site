import {Component, inject} from '@angular/core';
import {MemberService} from '../../../core/services/member-service';
import {ActivatedRoute, RouterLink, RouterLinkActive} from '@angular/router';
import {AsyncPipe, DatePipe} from '@angular/common';
import {async, Observable, switchMap} from 'rxjs';
import {Member} from '../../../types/member';
import {AuctionService} from '../../../core/services/auction-service';
import {AuctionCard} from '../../auctions/auction-card/auction-card';

@Component({
  selector: 'app-member-detailed',
  imports: [AsyncPipe, RouterLink, RouterLinkActive, DatePipe, AuctionCard],
  templateUrl: './member-detailed.html',
  styleUrl: './member-detailed.css',
})
export class MemberDetailed {
  private memberService = inject(MemberService);
  private auctionService = inject(AuctionService);
  private route = inject(ActivatedRoute);

  protected member$? = this.route.paramMap.pipe(
    switchMap(params => this.memberService.getMember(params.get('displayName')!))
  );

  protected auctions$ = this.route.paramMap.pipe(
    switchMap(params => this.auctionService.getAuctions(params.get('displayName')!))
  );
  protected readonly async = async;
}
