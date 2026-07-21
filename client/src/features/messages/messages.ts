import {Component, inject, signal} from '@angular/core';
import {DatePipe} from '@angular/common';
import {FormsModule, NgForm} from '@angular/forms';
import {Subject, of, debounceTime, distinctUntilChanged, switchMap} from 'rxjs';
import {MessageService} from '../../core/services/message-service';
import {MemberService} from '../../core/services/member-service';
import {AccountService} from '../../core/services/account-service';
import {ToastService} from '../../core/services/toast-service';
import {getApiErrorMessage} from '../../types/error';
import {Message} from '../../types/message';
import {Member} from '../../types/member';
import {PagedResponse} from '../../types/pagination';

type Container = 'Inbox' | 'Outbox';

@Component({
  selector: 'app-messages',
  imports: [DatePipe, FormsModule],
  templateUrl: './messages.html',
  styleUrl: './messages.css',
})
export class Messages {
  private messageService = inject(MessageService);
  private memberService = inject(MemberService);
  private accountService = inject(AccountService);
  private toast = inject(ToastService);

  protected container = signal<Container>('Inbox');
  protected pageNumber = signal(1);
  protected pageSize = 10;
  protected paginatedMessages = signal<PagedResponse<Message> | null>(null);

  protected selectedMessage = signal<Message | null>(null);

  protected showCompose = signal(false);
  protected compose = {recipientId: '', content: ''};
  protected replyContent = '';
  protected sending = signal(false);

  // Recipient autocomplete
  protected recipientQuery = '';
  protected searchResults = signal<Member[]>([]);
  protected selectedRecipient = signal<Member | null>(null);
  private searchTerms = new Subject<string>();

  constructor() {
    this.searchTerms.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      switchMap(term => term.trim() ? this.memberService.searchMembers(term.trim()) : of([])),
    ).subscribe(results => this.searchResults.set(results));
  }

  ngOnInit() {
    this.loadMessages();
  }

  loadMessages() {
    this.messageService.getMessages(this.container(), this.pageNumber(), this.pageSize).subscribe({
      next: response => this.paginatedMessages.set(response),
    });
  }

  setContainer(container: Container) {
    if (this.container() === container) return;
    this.container.set(container);
    this.pageNumber.set(1);
    this.loadMessages();
  }

  goToPage(page: number) {
    this.pageNumber.set(page);
    this.loadMessages();
  }

  openMessage(message: Message) {
    this.replyContent = '';
    this.selectedMessage.set(message);

    // Mark an inbox message read the first time it's opened, then sync local + nav state.
    if (this.container() === 'Inbox' && !message.dateRead) {
      this.messageService.markAsRead(message.id).subscribe({
        next: () => {
          message.dateRead = new Date().toISOString();
          this.messageService.refreshUnread();
        },
      });
    }
  }

  closeMessage() {
    this.replyContent = '';
    this.selectedMessage.set(null);
  }

  toggleCompose() {
    this.showCompose.update(open => !open);
    if (!this.showCompose()) this.clearRecipient();
  }

  onRecipientSearch(term: string) {
    // Typing invalidates any prior selection until a new one is chosen.
    this.selectedRecipient.set(null);
    this.compose.recipientId = '';
    this.searchTerms.next(term);
  }

  selectRecipient(member: Member) {
    this.selectedRecipient.set(member);
    this.compose.recipientId = member.id;
    this.recipientQuery = member.displayName;
    this.searchResults.set([]);
  }

  clearRecipient() {
    this.selectedRecipient.set(null);
    this.compose.recipientId = '';
    this.recipientQuery = '';
    this.searchResults.set([]);
  }

  sendMessage(form: NgForm) {
    if (form.invalid || !this.compose.recipientId || this.sending()) return;

    this.send(this.compose.recipientId, this.compose.content, 'Message sent', () => {
      this.compose = {recipientId: '', content: ''};
      form.resetForm();
      this.clearRecipient();
      this.showCompose.set(false);
      // A sent message only appears in the Outbox, so refresh only when it's showing.
      if (this.container() === 'Outbox') this.loadMessages();
    });
  }

  sendReply() {
    const message = this.selectedMessage();
    if (!message || !this.replyContent.trim() || this.sending()) return;

    this.send(this.replyRecipientId(message), this.replyContent, 'Reply sent', () => {
      this.replyContent = '';
    });
  }

  private send(recipientId: string, content: string, successMessage: string, onSuccess: () => void) {
    this.sending.set(true);
    this.messageService.createMessage(recipientId.trim(), content.trim()).subscribe({
      next: () => {
        this.toast.success(successMessage);
        onSuccess();
        this.sending.set(false);
      },
      error: err => {
        this.toast.error(getApiErrorMessage(err, 'Failed to send message'));
        this.sending.set(false);
      },
    });
  }

  // The reply goes to whichever party in the message isn't the current user.
  private replyRecipientId(message: Message): string {
    const myId = this.accountService.currentUser()?.id;
    return message.senderId === myId ? message.recipientId : message.senderId;
  }
}
