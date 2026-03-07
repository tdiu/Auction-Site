import {Component, inject} from '@angular/core';
import {MemberService} from '../../../core/services/member-service';
import {ActivatedRoute, RouterLink, RouterLinkActive} from '@angular/router';
import {AsyncPipe, DatePipe} from '@angular/common';
import {Observable, switchMap} from 'rxjs';
import {Member} from '../../../types/member';

@Component({
  selector: 'app-member-detailed',
  imports: [AsyncPipe, RouterLink, RouterLinkActive, DatePipe],
  templateUrl: './member-detailed.html',
  styleUrl: './member-detailed.css',
})
export class MemberDetailed {
  private memberService = inject(MemberService);
  private route = inject(ActivatedRoute);
  protected member$? = this.route.paramMap.pipe(
    switchMap(params => this.memberService.getMember(params.get('displayName')!))
  );
}
